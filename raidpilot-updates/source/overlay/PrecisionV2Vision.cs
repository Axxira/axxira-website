using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RaidPilot.Overlay;

/// <summary>
/// An immutable observation of local game-window pixels. No image or input
/// is persisted or sent to the relay: only motion statistics cross threads.
/// </summary>
internal readonly record struct PrecisionV2Frame(
    long CapturedAt,
    nint GameWindow,
    VisualMotionObservation Visual,
    double ProcessingMilliseconds);

/// <summary>
/// Precision V2: independent 160x90 sparse optical-flow capture on a worker.
/// The original 64x36 visual guard stays intact as a safe fallback.
/// GDI is the default; user-opted WGC is accepted only with verified
/// game-client geometry, otherwise it falls back to GDI.
/// </summary>
internal static class PrecisionV3ContinuityPolicy
{
    internal const int MaximumPendingPolls = 10;
    internal const double MaximumFlowGapSeconds = 0.58;
    internal const double MaximumStationaryVetoAgeSeconds = 0.30;

    // A short gap after valid WGC frames must not switch to GDI and destroy
    // optical-flow tracks. Before the first GPU frame GDI keeps operating.
    internal static bool ShouldHoldWgcOnPending(int activeBackend, int emptyPolls) =>
        activeBackend == 1
        && emptyPolls > 0
        && emptyPolls < MaximumPendingPolls;

    internal static bool MustReprimeAfterGap(long lastWgcFrameAt, long now) =>
        lastWgcFrameAt <= 0
        || now <= lastWgcFrameAt
        || (now - lastWgcFrameAt) / (double)Stopwatch.Frequency
            > MaximumFlowGapSeconds;
}

internal sealed class PrecisionV2VisualSampler : IDisposable
{
    internal const int Width = 160;
    internal const int Height = 90;
    internal const double CaptureIntervalSeconds = 0.20;
    internal const double MaximumFreshAgeSeconds = 0.48;

    // Larger search window and denser spatial consensus are isolated to V2.
    // Slow/ambiguous frames still fall back to V1 rather than making up motion.
    internal static VisualEgomotionSettings EnhancedFlowSettings { get; } = new()
    {
        HorizontalSearchRadius = 12,
        VerticalSearchRadius = 7,
        GridColumns = 10,
        GridRows = 6,
        MinimumTexturedFeatures = 12,
        MinimumMatchedFeatures = 8,
        MinimumInlierColumnSpan = 3,
        MinimumInlierRowSpan = 2,
        MinimumReliableConfidence = 0.60,
    };

    private readonly VisualMotionGate _gate = new(new VisualMotionGateSettings
    {
        SampleWidth = Width,
        SampleHeight = Height,
        EgomotionSettings = EnhancedFlowSettings,
    });
    private readonly object _sync = new();
    private readonly PrecisionV3WindowsCapture _windowsCapture = new();
    private int _gpuSelected;
    private int _gpuUnavailable;
    private int _gpuPendingFrames;
    private int _activeCaptureBackend = -1; // -1 unprimed, 0 GDI, 1 WGC
    private long _gpuFrames;
    private long _gpuFallbacks;
    private long _lastGpuObservationAt;
    private long _gpuHeldPendingPolls;
    private long _gpuLongGapReprimes;
    private long _geometryChanges;
    private long _geometryRejected;
    private nint _activeCaptureWindow;
    private string _gpuGeometryWarning = "aucun écart";
    private PrecisionV2Frame? _latest;
    private long _lastRequestedAt;
    private int _generation;
    private int _workerBusy;
    private int _resetOnNextCapture;
    private int _disposed;
    private long _captured;
    private long _failed;
    private long _reliableFrames;
    private long _unreliableFrames;
    private double _lastProcessingMilliseconds;
    private int _consecutiveFailures;

    internal long Captured => Interlocked.Read(ref _captured);
    internal long Failed => Interlocked.Read(ref _failed);
    internal long ReliableFrames => Interlocked.Read(ref _reliableFrames);
    internal long UnreliableFrames => Interlocked.Read(ref _unreliableFrames);
    internal bool IsBusy => Volatile.Read(ref _workerBusy) == 1;
    internal double LastProcessingMilliseconds => Volatile.Read(ref _lastProcessingMilliseconds);
    internal bool GpuCaptureSelected => Volatile.Read(ref _gpuSelected) != 0;
    internal long GpuFrames => Interlocked.Read(ref _gpuFrames);
    internal long GpuHeldPendingPolls => Interlocked.Read(ref _gpuHeldPendingPolls);
    internal long GpuLongGapReprimes => Interlocked.Read(ref _gpuLongGapReprimes);
    internal long GpuFallbacks => Interlocked.Read(ref _gpuFallbacks);
    internal long GeometryChanges => Interlocked.Read(ref _geometryChanges);
    internal long GeometryRejected => Interlocked.Read(ref _geometryRejected);
    internal string CameraGeometryDiagnostic => _gate.CameraGeometryDiagnostic;
    internal string GeometryWarning => Volatile.Read(ref _gpuGeometryWarning);
    internal string Backend =>
        !GpuCaptureSelected ? "GDI 160×90"
        : Volatile.Read(ref _gpuUnavailable) != 0 ? "WGC indisponible — retour GDI"
        : Volatile.Read(ref _activeCaptureBackend) == 1 ? "WGC + Direct3D11 160×90"
        : "WGC initialisation — GDI provisoire";
    internal string GpuStatus => _windowsCapture.Status;

    /// <summary>
    /// The only entry point for opting into window capture. Default is GDI.
    /// Switching either way clears old observations and stops the old session.
    /// </summary>
    internal void SelectGpuCapture(bool enabled)
    {
        Volatile.Write(ref _gpuSelected, enabled ? 1 : 0);
        Volatile.Write(ref _gpuUnavailable, 0);
        Volatile.Write(ref _gpuPendingFrames, 0);
        Reset();
        _windowsCapture.Reset(allowRetry: enabled);
    }

    internal void ConfigureVerticalFieldOfView(double verticalFov) =>
        _gate.ConfigureVerticalFieldOfView(verticalFov);

    internal void Request(nint gameWindow, long now)
    {
        if (gameWindow == nint.Zero
            || now <= 0
            || Volatile.Read(ref _disposed) != 0
            || Volatile.Read(ref _workerBusy) != 0)
            return;

        var previous = Volatile.Read(ref _lastRequestedAt);
        // When GDI is costly or repeatedly unavailable, reduce the capture
        // cadence rather than stealing a core from Tarkov. Freshness guards
        // automatically return to the original model if results arrive late.
        var interval = Volatile.Read(ref _consecutiveFailures) >= 3
            || Volatile.Read(ref _lastProcessingMilliseconds) > 65.0
            ? 0.38 : CaptureIntervalSeconds;
        if (previous > 0
            && now >= previous
            && (now - previous) / (double)Stopwatch.Frequency < interval)
            return;
        if (Interlocked.CompareExchange(ref _workerBusy, 1, 0) != 0) return;

        Volatile.Write(ref _lastRequestedAt, now);
        var generation = Volatile.Read(ref _generation);
        _ = Task.Run(() =>
        {
            try
            {
                if (Volatile.Read(ref _disposed) != 0
                    || generation != Volatile.Read(ref _generation)) return;

                if (Interlocked.Exchange(ref _resetOnNextCapture, 0) != 0
                    || _activeCaptureWindow != gameWindow)
                {
                    _gate.Reset();
                    _activeCaptureWindow = gameWindow;
                    Volatile.Write(ref _activeCaptureBackend, -1);
                    Volatile.Write(ref _lastGpuObservationAt, 0);
                }

                var started = Stopwatch.GetTimestamp();
                var succeeded = false;
                VisualMotionObservation observation = default;
                if (Volatile.Read(ref _gpuSelected) != 0
                    && Volatile.Read(ref _gpuUnavailable) == 0)
                {
                    var status = _windowsCapture.ReadWithGeometry(
                        gameWindow, out var frame);
                    if (status == WgcFrameStatus.Ready)
                    {
                        // Validate WGC ContentSize against the actual EFT client
                        // viewport. Window borders and DPI-scaled crops otherwise
                        // turn one player's mouse counts into a different yaw.
                        var hasClient = VisualMotionGate.GdiCaptureMethods.GetClientRect(
                            gameWindow, out var rect);
                        var clientWidth = rect.Right - rect.Left;
                        var clientHeight = rect.Bottom - rect.Top;
                        if (!hasClient
                            || !VisualCaptureGeometryPolicy.TryMatchViewport(
                                clientWidth, clientHeight,
                                frame.ContentWidth, frame.ContentHeight,
                                out var validated))
                        {
                            Volatile.Write(ref _gpuGeometryWarning,
                                "proportions du client et de WGC incompatibles — GDI");
                            Interlocked.Increment(ref _geometryRejected);
                            DisableGpuForSession();
                        }
                        else
                        {
                            var currentAt = Stopwatch.GetTimestamp();
                            var previouslyGpu =
                                Volatile.Read(ref _activeCaptureBackend) == 1;
                            var longGap = previouslyGpu
                                && PrecisionV3ContinuityPolicy.MustReprimeAfterGap(
                                    Volatile.Read(ref _lastGpuObservationAt), currentAt);
                            if (!previouslyGpu || longGap)
                            {
                                // Never compare GDI and WGC, or frames separated
                                // by an unobservably long render gap.
                                _gate.Reset();
                                Volatile.Write(ref _activeCaptureBackend, 1);
                                if (longGap)
                                    Interlocked.Increment(ref _gpuLongGapReprimes);
                            }
                            Volatile.Write(ref _lastGpuObservationAt, currentAt);
                            var previousGeometry = _gate.CameraGeometryDiagnostic;
                            observation = _gate.ObserveLuminanceFrame(
                                frame.Luminance, Width, Height,
                                validated.ClientWidth, validated.ClientHeight);
                            if (previousGeometry != _gate.CameraGeometryDiagnostic)
                                Interlocked.Increment(ref _geometryChanges);
                            Volatile.Write(ref _gpuGeometryWarning, "capture validée");
                            Interlocked.Increment(ref _gpuFrames);
                            Volatile.Write(ref _gpuPendingFrames, 0);
                            succeeded = true;
                        }
                    }
                    else if (status == WgcFrameStatus.Pending)
                    {
                        // Missing one or two GPU frames must not erase the
                        // established WGC scene reference by interleaving GDI.
                        // The old observation still expires after 480 ms.
                        var pending = Interlocked.Increment(ref _gpuPendingFrames);
                        if (pending >= PrecisionV3ContinuityPolicy.MaximumPendingPolls)
                            DisableGpuForSession();
                        else if (PrecisionV3ContinuityPolicy.ShouldHoldWgcOnPending(
                            Volatile.Read(ref _activeCaptureBackend), pending))
                        {
                            Interlocked.Increment(ref _gpuHeldPendingPolls);
                            return;
                        }
                    }
                    else
                    {
                        DisableGpuForSession();
                    }
                }

                if (!succeeded)
                {
                    if (Volatile.Read(ref _activeCaptureBackend) != 0)
                    {
                        _gate.Reset();
                        Volatile.Write(ref _activeCaptureBackend, 0);
                        Volatile.Write(ref _lastGpuObservationAt, 0);
                    }
                    succeeded = _gate.TryObserveWindow(gameWindow, out observation);
                }
                var finished = Stopwatch.GetTimestamp();
                if (!succeeded)
                {
                    Interlocked.Increment(ref _failed);
                    Interlocked.Increment(ref _consecutiveFailures);
                    return;
                }
                Volatile.Write(ref _lastProcessingMilliseconds,
                    (finished - started) * 1000.0 / Stopwatch.Frequency);
                Volatile.Write(ref _consecutiveFailures, 0);
                if (observation.Egomotion.IsReliable && observation.CaptureProvenLive)
                    Interlocked.Increment(ref _reliableFrames);
                else
                    Interlocked.Increment(ref _unreliableFrames);

                var result = new PrecisionV2Frame(
                    finished,
                    gameWindow,
                    observation,
                    (finished - started) * 1000.0 / Stopwatch.Frequency);
                lock (_sync)
                {
                    if (Volatile.Read(ref _disposed) == 0
                        && generation == Volatile.Read(ref _generation))
                        _latest = result;
                }
                Interlocked.Increment(ref _captured);
            }
            catch (Exception error) when (error is
                ObjectDisposedException or InvalidOperationException
                or ExternalException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                Interlocked.Increment(ref _failed);
                Interlocked.Increment(ref _consecutiveFailures);
            }
            finally
            {
                Volatile.Write(ref _workerBusy, 0);
            }
        });
    }

    private void DisableGpuForSession()
    {
        if (Interlocked.Exchange(ref _gpuUnavailable, 1) == 0)
            Interlocked.Increment(ref _gpuFallbacks);
        _windowsCapture.Reset();
    }

    internal PrecisionV2Frame? GetFresh(long now, nint window)
    {
        if (window == nint.Zero || now <= 0 || Volatile.Read(ref _disposed) != 0)
            return null;
        lock (_sync)
        {
            if (_latest is not { } sample
                || sample.GameWindow != window
                || sample.CapturedAt > now
                || (now - sample.CapturedAt) / (double)Stopwatch.Frequency
                    > MaximumFreshAgeSeconds)
                return null;
            return sample;
        }
    }

    internal void Reset()
    {
        Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _resetOnNextCapture, 1);
        Volatile.Write(ref _lastRequestedAt, 0);
        Volatile.Write(ref _gpuPendingFrames, 0);
        Volatile.Write(ref _lastGpuObservationAt, 0);
        lock (_sync) _latest = null;
        _windowsCapture.Reset();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _generation);
        lock (_sync) _latest = null;
        _windowsCapture.Dispose();
        _gate.Dispose();
    }
}

/// <summary>
/// Confidence-gated complementary fusion: a reliable visual stop can veto
/// dead-reckoned keyboard movement. Sparse monocular flow cannot provide
/// metres by itself and never fabricates a new absolute X/Z reference.
/// </summary>
internal sealed class PrecisionV2MotionFusion
{
    private const double MinimumConfidence = 0.78;
    private const double MinimumInlierRatio = 0.72;
    private const int MinimumInliers = 9;
    private const int ConsecutiveStationaryFrames = 3;
    private const double MaximumRecentMotionAgeSeconds = 3.0;

    private long _lastProcessedAt;
    private long _lastReliableMotionAt;
    private int _stationaryCount;
    private long _acceptedStops;
    private long _lastAcceptedStopAt;
    private long _rejectedFrames;
    internal string LastDecision { get; private set; } = "En attente de scènes fiables";
    internal long AcceptedStops => _acceptedStops;
    internal long RejectedFrames => _rejectedFrames;
    internal int ConfirmedStationaryFrames => _stationaryCount;

    internal void Reset()
    {
        _lastProcessedAt = 0;
        _lastReliableMotionAt = 0;
        _lastAcceptedStopAt = 0;
        _stationaryCount = 0;
        LastDecision = "Nouvelle session";
    }

    internal RealtimeMotionEvidence Fuse(
        PrecisionV2Frame? frame,
        long now,
        nint activeWindow,
        bool movementCommanded,
        bool directionStable,
        RealtimeMotionEvidence baseline)
    {
        if (frame is not { } observed
            || activeWindow == nint.Zero
            || observed.GameWindow != activeWindow
            || observed.CapturedAt > now
            || (now - observed.CapturedAt) / (double)Stopwatch.Frequency
                > PrecisionV2VisualSampler.MaximumFreshAgeSeconds)
        {
            _stationaryCount = 0;
            LastDecision = "Trame absente/périmée : suivi classique";
            return baseline;
        }

        // During rotation, aiming or idle, visual stationarity is not
        // evidence of a blocked locomotion command. Discard the accumulated
        // stop counter so it cannot be reused on the next forward press.
        if (!movementCommanded || !directionStable)
        {
            _stationaryCount = 0;
            LastDecision = "Rotation, visée ou repos : suivi classique";
            return baseline;
        }

        var v = observed.Visual;
        var flow = v.Egomotion;
        var reliable = v.CaptureSucceeded
            && v.CaptureProvenLive
            && flow.IsReliable
            && flow.Confidence >= MinimumConfidence
            && flow.InlierRatio >= MinimumInlierRatio
            && flow.InlierFeatureCount >= MinimumInliers
            && flow.ResidualPixels <= 1.0
            && v.TextureStandardDeviation >= 10.0;

        if (_lastProcessedAt != observed.CapturedAt)
        {
            _lastProcessedAt = observed.CapturedAt;
            if (reliable && flow.State == VisualEgomotionState.Tracking)
            {
                _lastReliableMotionAt = observed.CapturedAt;
                _stationaryCount = 0;
                LastDecision = "Flux optique cohérent : mouvement observé";
            }
            else if (reliable && flow.State == VisualEgomotionState.Stationary
                && v.State == VisualMotionState.Stationary)
            {
                _stationaryCount++;
                LastDecision = "Décor immobile confirmé "
                    + _stationaryCount + "/" + ConsecutiveStationaryFrames;
            }
            else
            {
                _stationaryCount = 0;
                _rejectedFrames++;
                LastDecision = "Flux insuffisamment fiable : suivi classique";
            }
        }


        var recentMotion = _lastReliableMotionAt != 0
            && observed.CapturedAt >= _lastReliableMotionAt
            && (observed.CapturedAt - _lastReliableMotionAt)
                / (double)Stopwatch.Frequency <= MaximumRecentMotionAgeSeconds;
        // A last valid still-image cannot indefinitely veto the keyboard
        // while GPU frames are delayed. The display may retain a slightly
        // older observation, but stopping the player's pose requires a much
        // fresher stationary measurement.
        var recentStationaryImage =
            (now - observed.CapturedAt) / (double)Stopwatch.Frequency
                <= PrecisionV3ContinuityPolicy.MaximumStationaryVetoAgeSeconds;
        if (reliable
            && flow.State == VisualEgomotionState.Stationary
            && v.State == VisualMotionState.Stationary
            && _stationaryCount >= ConsecutiveStationaryFrames
            && recentMotion
            && recentStationaryImage)
        {
            // Stop only within a short window of previously confirmed motion.
            // A frozen/textureless render or a permanently static capture will
            // never silently block movement for the whole raid.
            if (_lastAcceptedStopAt != observed.CapturedAt)
            {
                _acceptedStops++;
                _lastAcceptedStopAt = observed.CapturedAt;
            }
            LastDecision = "Collision/arrêt visuel probable : vitesse annulée";
            return RealtimeMotionEvidence.Stationary;
        }

        if (reliable && flow.State == VisualEgomotionState.Stationary
            && !recentStationaryImage)
            LastDecision = "arrêt trop ancien : suivi clavier conservé";

        // If the old 64x36 guard called Stationary while the higher resolution
        // flow actually tracks, the safest result is Unknown (never fabricate
        // absolute metres or assert locomotion from rotation alone).
        if (reliable && flow.State == VisualEgomotionState.Tracking
            && baseline == RealtimeMotionEvidence.Stationary)
            return RealtimeMotionEvidence.Unknown;

        return baseline;
    }
}
