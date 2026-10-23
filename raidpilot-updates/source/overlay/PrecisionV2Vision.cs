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
/// This is a high-resolution GDI backend; it is not Windows.Graphics.Capture.
/// </summary>
internal sealed class PrecisionV2VisualSampler : IDisposable
{
    internal const int Width = 160;
    internal const int Height = 90;
    internal const double CaptureIntervalSeconds = 0.20;
    internal const double MaximumFreshAgeSeconds = 0.48;

    private readonly VisualMotionGate _gate = new(new VisualMotionGateSettings
    {
        SampleWidth = Width,
        SampleHeight = Height,
    });
    private readonly object _sync = new();
    private PrecisionV2Frame? _latest;
    private long _lastRequestedAt;
    private int _generation;
    private int _workerBusy;
    private int _resetOnNextCapture;
    private int _disposed;
    private long _captured;
    private long _failed;
    private double _lastProcessingMilliseconds;
    private int _consecutiveFailures;

    internal long Captured => Interlocked.Read(ref _captured);
    internal long Failed => Interlocked.Read(ref _failed);
    internal bool IsBusy => Volatile.Read(ref _workerBusy) == 1;
    internal double LastProcessingMilliseconds => Volatile.Read(ref _lastProcessingMilliseconds);
    internal string Backend => "GDI 160×90 (GPU non activé)";

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

                if (Interlocked.Exchange(ref _resetOnNextCapture, 0) != 0)
                    _gate.Reset();

                var started = Stopwatch.GetTimestamp();
                var succeeded = _gate.TryObserveWindow(gameWindow, out var observation);
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
        lock (_sync) _latest = null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _generation);
        lock (_sync) _latest = null;
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
        if (reliable
            && flow.State == VisualEgomotionState.Stationary
            && v.State == VisualMotionState.Stationary
            && _stationaryCount >= ConsecutiveStationaryFrames
            && recentMotion)
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

        // If the old 64x36 guard called Stationary while the higher resolution
        // flow actually tracks, the safest result is Unknown (never fabricate
        // absolute metres or assert locomotion from rotation alone).
        if (reliable && flow.State == VisualEgomotionState.Tracking
            && baseline == RealtimeMotionEvidence.Stationary)
            return RealtimeMotionEvidence.Unknown;

        return baseline;
    }
}
