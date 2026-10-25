using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>
/// Deterministic confidence arbitration for existing visual and input sensors.
/// Never guesses absolute coordinates. Repeated contradictory/stale samples
/// degrade confidence instead of producing false movement or blocked poses.
/// </summary>
internal sealed class RaidPilotIntelligenceEngine
{
    private long _lastFrameAt;
    private int _contradictions;
    internal long Accepted { get; private set; }
    internal long Rejected { get; private set; }
    internal string LastDecision { get; private set; } = "En attente d'observations";
    internal int Contradictions => _contradictions;

    internal void Reset()
    {
        _lastFrameAt = 0;
        _contradictions = 0;
        LastDecision = "Nouvelle session";
    }

    internal RealtimeMotionEvidence Evaluate(
        PrecisionV2Frame? frame, long now, nint gameWindow,
        bool commanded, bool stableDirection,
        RealtimeMotionEvidence baseline, RealtimeMotionEvidence proposed)
    {
        if (frame is not { } f || now < f.CapturedAt
            || f.GameWindow != gameWindow || gameWindow == nint.Zero
            || (now-f.CapturedAt)/(double)Stopwatch.Frequency > 0.30)
        {
            _contradictions=0;
            LastDecision="Capture absente ou périmée : modèle de base";
            return baseline;
        }

        if (f.CapturedAt <= _lastFrameAt)
            return proposed == RealtimeMotionEvidence.Stationary && _contradictions >= 2
                ? RealtimeMotionEvidence.Unknown : proposed;
        _lastFrameAt=f.CapturedAt;
        var v=f.Visual;
        var flow=v.Egomotion;
        var reliable=v.CaptureSucceeded && v.CaptureProvenLive
            && flow.IsReliable && flow.Confidence >= 0.80
            && flow.InlierRatio >= 0.74 && flow.InlierFeatureCount >= 9
            && flow.ResidualPixels <= 0.85
            && f.ProcessingMilliseconds <= 90;
        if (!reliable)
        {
            _contradictions=0;Rejected++;
            LastDecision="Vision non fiable : modèle de base";
            return baseline;
        }

        if (commanded && stableDirection
            && proposed==RealtimeMotionEvidence.Stationary
            && flow.State==VisualEgomotionState.Stationary)
        {
            _contradictions=0;Accepted++;
            LastDecision="Arrêt confirmé par plusieurs images";
            return proposed;
        }
        if (commanded && flow.State==VisualEgomotionState.Tracking)
        {
            _contradictions=0;Accepted++;
            LastDecision="Déplacement visuel confirmé";
            return proposed==RealtimeMotionEvidence.Stationary
                ? RealtimeMotionEvidence.Moving : proposed;
        }
        if (commanded && proposed==RealtimeMotionEvidence.Stationary
            && (flow.State!=VisualEgomotionState.Stationary || !stableDirection))
        {
            _contradictions++;Rejected++;
            LastDecision="Capteurs contradictoires : arrêt refusé";
            return RealtimeMotionEvidence.Unknown;
        }
        _contradictions=0;
        LastDecision="Observations compatibles";
        return proposed;
    }
}
