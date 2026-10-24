using System.Diagnostics;
using System.Windows.Forms;

namespace RaidPilot.Overlay;

/// <summary>
/// Reconciles the independent 160x90 visual reader with timestamped raw mouse
/// input. A scene shift is NOT an absolute position or a displacement in metres.
/// Only stationary, hip-fire, non-interaction turning may inform yaw calibration.
/// Captures stay in RAM; no EFT memory access, injection, PNG writes or network.
/// </summary>
internal sealed class VisualReaderYawSync
{
    private long _previousFrameAt;
    private long _lastAcceptedFrameAt;
    private bool _sawMovement;
    private bool _sawAiming;
    private bool _sawFreeLookOrInteraction;

    internal long Accepted { get; private set; }
    internal long Rejected { get; private set; }
    internal string LastStatus { get; private set; } = "en attente de deux images";
    internal const double MaximumFrameAgeSeconds = 0.30;
    internal const double MaximumIntervalSeconds = 0.50;
    internal const double MaximumProcessingMilliseconds = 75.0;

    internal void Reset()
    {
        _previousFrameAt = _lastAcceptedFrameAt = 0;
        _sawMovement = _sawAiming = _sawFreeLookOrInteraction = false;
        LastStatus = "nouvelle fenêtre ou session";
    }

    internal void ObserveActivity(bool moving, bool aiming, bool freeLookOrInteraction)
    {
        _sawMovement |= moving;
        _sawAiming |= aiming;
        _sawFreeLookOrInteraction |= freeLookOrInteraction;
    }

    internal bool HasRecentReliableFrame(long now) =>
        _lastAcceptedFrameAt > 0 && now >= _lastAcceptedFrameAt
        && (now - _lastAcceptedFrameAt) / (double)Stopwatch.Frequency <= 0.65;

    internal bool TryRead(
        PrecisionV2Frame? incoming, long now, nint gameWindow,
        RawKeyboardTimeline keyboard, PrecisionYawTimeline mouse,
        ReadOnlySpan<Keys> movementKeys, out long intervalStart, out long intervalEnd)
    {
        intervalStart = intervalEnd = 0;
        if (incoming is not { } frame || gameWindow == nint.Zero
            || frame.GameWindow != gameWindow || now < frame.CapturedAt
            || frame.CapturedAt <= 0 || frame.CapturedAt <= _previousFrameAt)
            return false; // The UI can encounter one completed frame on several ticks.

        var previous = _previousFrameAt;
        _previousFrameAt = frame.CapturedAt;
        var sawInput = _sawMovement || _sawAiming || _sawFreeLookOrInteraction;
        _sawMovement = _sawAiming = _sawFreeLookOrInteraction = false;

        var seconds = previous > 0
            ? (frame.CapturedAt - previous) / (double)Stopwatch.Frequency
            : double.PositiveInfinity;
        var age = (now - frame.CapturedAt) / (double)Stopwatch.Frequency;
        var visual = frame.Visual.Egomotion;
        if (previous <= 0 || seconds is < 0.12 or > MaximumIntervalSeconds
            || age > MaximumFrameAgeSeconds
            || !double.IsFinite(frame.ProcessingMilliseconds)
            || frame.ProcessingMilliseconds > MaximumProcessingMilliseconds
            || sawInput || !frame.Visual.CaptureSucceeded
            || !frame.Visual.CaptureProvenLive
            || visual.State != VisualEgomotionState.Tracking
            || visual.Confidence < 0.78 || visual.InlierRatio < 0.72
            || visual.InlierFeatureCount < 9 || visual.ResidualPixels > 0.75
            || Math.Abs(visual.SceneShiftY) > 1.1)
        {
            Rejected++;
            LastStatus = sawInput ? "mouvement, visée ou interaction: yaw ignoré"
                : "flux trop ancien, discontinu ou peu fiable";
            return false;
        }

        // Check the ENTIRE capture-to-capture interval, not just the current UI
        // tick. Brief W/A/S/D taps must never be mistaken for a pure camera turn.
        foreach (var key in movementKeys)
        {
            if (!keyboard.TrySample(key, previous, frame.CapturedAt, out var keyState)
                || keyState.HeldTicks > 0 || keyState.IsDown
                || keyState.WasPressed || keyState.WasReleased)
            {
                Rejected++;
                LastStatus = "déplacement clavier dans l'intervalle";
                return false;
            }
        }

        if (!mouse.TrySummarizeInterval(previous, frame.CapturedAt, out var summary)
            || summary.Samples == 0 || summary.Counts == 0
            || summary.Magnitude < 5 || summary.MixedAiming || summary.Aiming
            || Math.Abs(summary.Counts) < summary.Magnitude * 0.90)
        {
            Rejected++;
            LastStatus = "rotation souris incomplète, ADS ou ambiguë";
            return false;
        }

        _lastAcceptedFrameAt = frame.CapturedAt;
        Accepted++;
        LastStatus = "yaw visuel 160x90 vérifié, sans translation";
        intervalStart = previous;
        intervalEnd = frame.CapturedAt;
        return true;
    }
}
