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
    private nint _lastWindow;
    private int _stationaryFrames;
    private RealtimeMotionEvidence _lastDecisionEvidence;
    private int _contradictions;
    internal long Accepted { get; private set; }
    internal long Rejected { get; private set; }
    internal string LastDecision { get; private set; } = "En attente d'observations";
    internal int Contradictions => _contradictions;

    internal void Reset()
    {
        _lastFrameAt = 0;
        _lastWindow = nint.Zero;
        _stationaryFrames = 0;
        _lastDecisionEvidence = RealtimeMotionEvidence.Unknown;
        _contradictions = 0;
        LastDecision = "Nouvelle session";
    }

    internal RealtimeMotionEvidence Evaluate(
        PrecisionV2Frame? frame, long now, nint gameWindow,
        bool commanded, bool stableDirection,
        RealtimeMotionEvidence baseline, RealtimeMotionEvidence proposed)
    {
        if (gameWindow != _lastWindow)
        {
            Reset();
            _lastWindow = gameWindow;
        }
        if (!commanded || !stableDirection)
        {
            _stationaryFrames = 0;
            _lastDecisionEvidence = RealtimeMotionEvidence.Unknown;
            return baseline == RealtimeMotionEvidence.Stationary
                ? baseline : proposed == RealtimeMotionEvidence.Stationary
                    ? baseline : proposed;
        }
        if (frame is not { } f || now < f.CapturedAt
            || f.GameWindow != gameWindow || gameWindow == nint.Zero
            || (now-f.CapturedAt)/(double)Stopwatch.Frequency > 0.30)
        {
            _stationaryFrames = 0;
            _lastDecisionEvidence = baseline;
            LastDecision="Trame absente ou périmée : modèle de base";
            return baseline;
        }
        // A worker frame is used at most once for fresh evidence. Never
        // promote a repeated old frame into a stronger stop decision.
        if (f.CapturedAt <= _lastFrameAt)
        {
            // Never replay a previous visual decision as fresh evidence. In
            // particular, a repeated stationary frame must not override new
            // movement reported by the baseline input estimator.
            Rejected++;
            LastDecision = "Trame déjà traitée : retour au modèle de base";
            return _lastDecisionEvidence = baseline;
        }
        if (_lastFrameAt > 0 &&
            (f.CapturedAt - _lastFrameAt)/(double)Stopwatch.Frequency > 0.65)
            _stationaryFrames = 0;
        _lastFrameAt=f.CapturedAt;
        var v=f.Visual;
        var flow=v.Egomotion;
        var reliable=v.CaptureSucceeded && v.CaptureProvenLive
            && flow.IsReliable && flow.Confidence >= 0.80
            && flow.InlierRatio >= 0.74 && flow.InlierFeatureCount >= 9
            && double.IsFinite(flow.ResidualPixels) && flow.ResidualPixels <= 0.85
            && double.IsFinite(f.ProcessingMilliseconds)
            && f.ProcessingMilliseconds >= 0 && f.ProcessingMilliseconds <= 90;
        if (!reliable)
        {
            _stationaryFrames=0;Rejected++;
            LastDecision="Vision non fiable : modèle de base";
            return _lastDecisionEvidence=baseline;
        }
        if (flow.State==VisualEgomotionState.Tracking)
        {
            _stationaryFrames=0;Accepted++;
            LastDecision="Déplacement visuel confirmé";
            return _lastDecisionEvidence=proposed==RealtimeMotionEvidence.Stationary
                ? RealtimeMotionEvidence.Moving : proposed;
        }
        if (flow.State==VisualEgomotionState.Stationary
            && v.State==VisualMotionState.Stationary)
        {
            _stationaryFrames++;
            if (_stationaryFrames>=3 && proposed==RealtimeMotionEvidence.Stationary)
            {
                Accepted++;
                LastDecision="Arrêt corroboré par trois trames distinctes";
                return _lastDecisionEvidence=RealtimeMotionEvidence.Stationary;
            }
            LastDecision="Confirmation arrêt "+_stationaryFrames+"/3";
            return _lastDecisionEvidence=baseline;
        }
        _stationaryFrames=0;Rejected++;_contradictions++;
        LastDecision="Preuves contradictoires : repli prudent";
        return _lastDecisionEvidence=baseline;
    }
}
