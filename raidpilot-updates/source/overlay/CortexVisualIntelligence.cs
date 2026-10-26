using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>
/// CORTEX V4: confidence-weighted visual/odometry fusion and cautious
/// place-recovery verification. Reuses the existing persistent low-/high-res
/// visual keyframe recognizers; it does not synthesize EFT coordinates.
///
/// Only local, corroborated repeat observations may unlock a large visual
/// loop closure. Uncertain events request an exact native F8 instead.
/// </summary>
internal sealed class CortexVisualIntelligence
{
    private const double MaximumFastRecoveryMeters = 6.0;
    private const double MaximumVisualJumpMeters = 18.0;
    private const double LoopVerificationWindowSeconds = 3.0;
    private const double LoopMaximumPoseDisagreementMeters = 1.5;
    private const double LoopMaximumYawDisagreementDegrees = 12.0;

    private string _map = string.Empty;
    private int _raid = -1;
    private nint _window;
    private LoopCandidate? _pending;
    private long _accepted;
    private long _rejected;
    private long _loopsVerified;
    private string _lastReason = "Attente de repères visuels validés";
    private CortexRecoveryMode _mode = CortexRecoveryMode.WaitingForAnchor;

    internal long Accepted => _accepted;
    internal long Rejected => _rejected;
    internal long VerifiedLoopClosures => _loopsVerified;
    internal CortexRecoveryMode Mode => _mode;
    internal string Reason => _lastReason;

    internal string Diagnostic =>
        $"V4 vision : {_mode} · {_lastReason}"
        + $"\nFusion acceptée {_accepted}, rejetée {_rejected}, "
        + $"retours visuels corroborés {_loopsVerified}";

    internal void Reset()
    {
        _map = string.Empty;
        _raid = -1;
        _window = nint.Zero;
        _pending = null;
        _accepted = _rejected = _loopsVerified = 0;
        _mode = CortexRecoveryMode.WaitingForAnchor;
        _lastReason = "Attente du premier F8 réel";
    }

    internal void OnNativeF8(string map, int raid, nint window)
    {
        SetSession(map, raid, window);
        _pending = null;
        _mode = CortexRecoveryMode.Tracking;
        _lastReason = "F8 réel confirmé";
    }

    /// <summary>
    /// Return null only when a proposed relocalization is unsafe; callers
    /// retain ordinary motion prediction and the existing F8 recovery policy.
    /// A change of raid or window never carries pending loop evidence over.
    /// </summary>
    internal VisualRelocalizationDecision? Filter(
        VisualRelocalizationDecision proposal,
        RealtimePose predictedAtCapture,
        string map,
        int raid,
        nint window,
        long now,
        bool fromSharedAtlas,
        long? lowKeyframeId,
        long? superKeyframeId)
    {
        if (!proposal.ShouldCorrect || proposal.TargetPose is not { } target
            || !Finite(predictedAtCapture) || !Finite(target)
            || !double.IsFinite(proposal.Confidence)
            || proposal.Confidence is < 0 or > 1
            || !double.IsFinite(proposal.PositionGain)
            || !double.IsFinite(proposal.YawGain)
            || proposal.PositionGain is < 0 or > 1
            || proposal.YawGain is < 0 or > 1
            || !double.IsFinite(proposal.MaximumTranslationStepMeters)
            || !double.IsFinite(proposal.MaximumYawStepDegrees)
            || proposal.MaximumTranslationStepMeters <= 0
            || proposal.MaximumYawStepDegrees <= 0
            || now <= 0 || window == nint.Zero || string.IsNullOrWhiteSpace(map))
            return Reject("Preuve visuelle ou géométrie invalide");

        SetSession(map, raid, window);
        var delta = Math.Sqrt(Math.Pow(target.X - predictedAtCapture.X, 2)
            + Math.Pow(target.Z - predictedAtCapture.Z, 2));
        var vertical = Math.Abs(target.Y - predictedAtCapture.Y);
        var yaw = AngleDistance(target.YawDegrees,
            predictedAtCapture.YawDegrees);
        if (vertical > 3.0 || yaw > 115.0
            || delta > MaximumVisualJumpMeters)
        {
            return Reject("Écart visuel non corroboré : F8 réel préférable");
        }

        // A very large pose change is not a safe single-frame correction.
        // Require a repeated, source-independent local high/low landmark
        // consensus before permitting a bounded loop recovery.
        var largeLoop = proposal.GlobalLoopClosure
            || delta > MaximumFastRecoveryMeters;
        if (largeLoop)
        {
            var reliable = !fromSharedAtlas
                && proposal.Source == VisualRelocalizationSource.Consensus
                && proposal.Confidence >= 0.95
                && lowKeyframeId is > 0 && superKeyframeId is > 0
                && yaw <= 40.0;
            if (!reliable)
                return Reject("Récupération visuelle : double repère local nécessaire");

            if (_pending is not { } prior
                || prior.LowId != lowKeyframeId.Value
                || prior.SuperId != superKeyframeId.Value
                || now <= prior.At
                || (now - prior.At) / (double)Stopwatch.Frequency
                    > LoopVerificationWindowSeconds
                || Math.Sqrt(Math.Pow(prior.Pose.X - target.X, 2)
                    + Math.Pow(prior.Pose.Z - target.Z, 2))
                    > LoopMaximumPoseDisagreementMeters
                || Math.Abs(prior.Pose.Y - target.Y) > 1.0
                || AngleDistance(prior.Pose.YawDegrees, target.YawDegrees)
                    > LoopMaximumYawDisagreementDegrees)
            {
                _pending = new(lowKeyframeId.Value, superKeyframeId.Value,
                    target, now);
                _mode = CortexRecoveryMode.AwaitingVisualConsensus;
                return Reject("Boucle candidate : seconde confirmation requise",
                    keepMode: true);
            }

            _pending = null;
            _loopsVerified++;
            _mode = CortexRecoveryMode.Recovering;
        }
        else
        {
            _pending = null;
        }

        // Scalar Kalman-like blend. Variances are heuristic conservative
        // noise proxies, NOT a calibrated full EKF or an independent GPS fix.
        // Never increase the original relocalizer's correction strengths.
        var predictedSigma = Math.Clamp(
            predictedAtCapture.PositionUncertaintyMeters, 0.15, 12.0);
        var visualSigma = 0.55 + 4.5 * (1.0 - proposal.Confidence)
            + (proposal.Source == VisualRelocalizationSource.Consensus ? 0 : 1.0)
            + (fromSharedAtlas ? 1.5 : 0);
        var positionWeight = predictedSigma * predictedSigma /
            (predictedSigma * predictedSigma + visualSigma * visualSigma);
        var yawSigma = Math.Clamp(
            predictedAtCapture.YawUncertaintyDegrees, 1.0, 50.0);
        var observedYawSigma = 3.0 + 24.0 * (1.0 - proposal.Confidence)
            + (fromSharedAtlas ? 8.0 : 0);
        var yawWeight = yawSigma * yawSigma /
            (yawSigma * yawSigma + observedYawSigma * observedYawSigma);
        var result = proposal with
        {
            PositionGain = Math.Min(proposal.PositionGain, positionWeight),
            YawGain = Math.Min(proposal.YawGain, yawWeight),
            MaximumTranslationStepMeters = Math.Min(
                proposal.MaximumTranslationStepMeters,
                largeLoop ? 0.75 : 2.0),
            MaximumYawStepDegrees = Math.Min(
                proposal.MaximumYawStepDegrees,
                largeLoop ? 6.0 : 12.0),
        };
        _lastReason = largeLoop
            ? "Retour sur lieu connu confirmé; correction limitée"
            : "Vision locale fusionnée avec l'incertitude de déplacement";
        return result;
    }

    internal void NoteApplied()
    {
        _accepted++;
        if (_mode != CortexRecoveryMode.Recovering)
            _mode = CortexRecoveryMode.Tracking;
    }

    internal void OnTrackingUncertain()
    {
        _pending = null;
        _mode = CortexRecoveryMode.NeedsNativeF8;
        _lastReason = "Localisation incertaine : requérir un F8 réel sûr";
    }

    private VisualRelocalizationDecision? Reject(string reason,
        bool keepMode = false)
    {
        _rejected++;
        _lastReason = reason;
        if (!keepMode)
        {
            _pending = null;
            _mode = CortexRecoveryMode.NeedsNativeF8;
        }
        return null;
    }

    private void SetSession(string map, int raid, nint window)
    {
        if (map == _map && raid == _raid && window == _window)
            return;
        _map = map;
        _raid = raid;
        _window = window;
        _pending = null;
        _mode = CortexRecoveryMode.WaitingForAnchor;
    }

    private static bool Finite(RealtimePose p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y)
        && double.IsFinite(p.Z) && double.IsFinite(p.YawDegrees)
        && double.IsFinite(p.PositionUncertaintyMeters)
        && double.IsFinite(p.YawUncertaintyDegrees)
        && p.PositionUncertaintyMeters >= 0 && p.YawUncertaintyDegrees >= 0;

    private static bool Finite(VisualKeyframePose p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y)
        && double.IsFinite(p.Z) && double.IsFinite(p.YawDegrees);

    private static double AngleDistance(double a, double b) =>
        Math.Abs(((a - b + 540.0) % 360.0 + 360.0) % 360.0 - 180.0);

    private readonly record struct LoopCandidate(
        long LowId, long SuperId, VisualKeyframePose Pose, long At);
}

internal enum CortexRecoveryMode
{
    WaitingForAnchor,
    Tracking,
    AwaitingVisualConsensus,
    Recovering,
    NeedsNativeF8,
}

/// <summary>
/// An extra conservative recovery opportunity only while the player is truly
/// idle and ordinary safe screenshot guards are still enforced by OverlayForm.
/// Never bypasses the user's auto-reanchor preference, action/aim/fire checks,
/// foreground window or native screenshot hotkey availability.
/// </summary>
internal static class CortexRecoveryPolicy
{
    internal static bool ShouldRequestNativeF8(
        bool cortexEnabled, bool autoRecoveryEnabled,
        bool activeGameplay, CortexRecoveryMode mode,
        double uncertaintyMeters, double idleSeconds,
        double secondsSinceLastAttempt, double secondsSinceNativeF8)
    {
        return cortexEnabled && autoRecoveryEnabled && activeGameplay
            && mode == CortexRecoveryMode.NeedsNativeF8
            && double.IsFinite(uncertaintyMeters)
            && double.IsFinite(idleSeconds)
            && !double.IsNaN(secondsSinceLastAttempt)
            && double.IsFinite(secondsSinceNativeF8)
            && uncertaintyMeters >= 1.5 && uncertaintyMeters <= 200
            && idleSeconds >= 0.90
            && secondsSinceLastAttempt >= 12.0
            && secondsSinceNativeF8 >= 8.0;
    }
}
