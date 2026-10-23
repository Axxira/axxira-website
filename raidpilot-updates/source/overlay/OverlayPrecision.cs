using System.Diagnostics;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private readonly PrecisionYawTimeline _precisionYaw = new();
    private readonly PrecisionMotionIntegrator _precisionMotion = new();
    private readonly PrecisionCalibration _precisionCalibration = new();
    private readonly PrecisionResiduals _precisionResiduals = new();
    private readonly VisualYawCalibration _visualYawCalibration = new();
    private readonly System.Windows.Forms.Timer _precisionUiTimer = new() { Interval = 1000 };
    private readonly ToolStripMenuItem _precisionModeMenu = new("Suivi précis — séquences courtes") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _precisionStatusMenu = new($"RaidPilot {Application.ProductVersion} — Précision GPS");
    private bool _precisionSegmentClean = true;
    private int _precisionYawActivity;
    private long _precisionTicks, _precisionFallbacks, _precisionGaps;
    private int _precisionLastSlices;
    private double _precisionLastComputeMs;
    private string _precisionLastFallback = "aucun";

    private void InitializePrecision()
    {
        if (_trayIcon.ContextMenuStrip is { } menu)
        {
            menu.Items.Insert(0, _precisionStatusMenu);
            menu.Items.Insert(1, _precisionModeMenu);
        }
        _precisionModeMenu.Click += (_, _) =>
        {
            _precisionYaw.Reset();
            _precisionSegmentClean = false;
            _realtimeEstimator.SuspendMotion();
        };
        _precisionStatusMenu.Click += (_, _) =>
        {
            var last = _precisionResiduals.Last;
            var residual = last is { } e ? $"{e.Meters:F2} m / {e.YawDegrees:F1}°" : "aucune comparaison native dans ce raid";
            var median = _precisionResiduals.MedianMeters is { } m ? $"{m:F2} m" : "non mesurée";
            var uncertainty = _realtimeEstimator.CurrentPose is { } p ? $"{p.PositionUncertaintyMeters:F1} m (modèle, pas une garantie)" : "sans position de référence";
            MessageBox.Show(this,
                $"RaidPilot {Application.ProductVersion} — Diagnostic de précision\n\n"
                + $"Dernier écart AVANT recalage : {residual}\n"
                + $"Médiane sur {_precisionResiduals.Count} recalages récents : {median}\n"
                + $"Incertitude estimée : {uncertainty}\n"
                + $"Intégrations fines : {_precisionTicks} ; replis : {_precisionFallbacks}\n"
                + $"Dernier calcul : {_precisionLastComputeMs:F3} ms ; {_precisionLastSlices} sous-pas\n"
                + $"Interruptions : {_precisionGaps} ; dernier repli : {_precisionLastFallback}\n"
                + $"Calibration F8 : {_precisionCalibration.LastDecision}\n"
                + $"Calibration yaw vision : {_visualYawCalibration.LastDecision}\n"
                + $"FOV visuel effectif : {_visualMotionGate.EgomotionHorizontalFieldOfViewDegrees:F1}°\n"
                + $"Precision V2 : {PrecisionV2Diagnostic()}\n\n"
                + "Les écarts sont mesurés aux positions de référence reçues, pas en continu. "
                + "Les sous-pas de 1 à 5 ms sont des calculs en mémoire, pas des captures. "
                + "Le suivi ne connaît pas automatiquement chaque collision, épuisement ou changement d'étage.",
                "RaidPilot — Précision", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _precisionUiTimer.Tick += (_, _) =>
        {
            if (_formLifetime.IsCancellationRequested || IsDisposed) return;
            _precisionStatusMenu.Text = _realtimeEstimator.IsAnchored
                ? $"RaidPilot {Application.ProductVersion} — GPS estimé · diagnostic"
                : $"RaidPilot {Application.ProductVersion} — GPS en attente de position";
        };
        _precisionUiTimer.Start();
        InitializePrecisionV2();
    }

    private bool TryAdvancePrecision(long start, long end, bool sprint, bool slow, double scale,
        RealtimeMotionEvidence evidence, int mouseCounts, out PrecisionMotionResult result)
    {
        result = default;
        if (!_precisionModeMenu.Checked || !_rawKeyboardInputRegistered) return false;
        var begin = Stopwatch.GetTimestamp();
        var success = _precisionMotion.TryAdvance(_realtimeEstimator, _rawKeyboardTimeline, _precisionYaw,
            start, end, _controlSettings.MoveForward, _controlSettings.MoveBackward,
            _controlSettings.MoveLeft, _controlSettings.MoveRight, sprint, slow, scale, evidence, mouseCounts, out result);
        _precisionLastComputeMs = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
        if (success) { _precisionTicks++; _precisionLastSlices = result.Slices; }
        else { _precisionFallbacks++; _precisionLastSlices = 0; _precisionLastFallback = _precisionMotion.LastFallback; }
        return success;
    }

    private void TryCalibrateYawFromVision(
        long intervalStart,
        long intervalEnd,
        VisualMotionObservation visualObservation,
        bool movementActive,
        bool aiming)
    {
        if (intervalStart <= 0
            || intervalEnd <= intervalStart
            || !_precisionYaw.TrySummarizeInterval(
                intervalStart,
                intervalEnd,
                out var yawSummary))
        {
            return;
        }

        var settings = _realtimeEstimator.Settings;
        var visualContextAiming = aiming || yawSummary.Aiming || yawSummary.MixedAiming;
        var realtimeCorrection = _visualYawCalibration.TryGetRealtimeCorrection(
            visualObservation.Egomotion,
            yawSummary.Counts,
            yawSummary.Magnitude,
            movementActive,
            visualContextAiming,
            _middleMouseDown,
            settings.HipYawDegreesPerMouseCount);
        if (realtimeCorrection is { } correction
            && _realtimeEstimator.IsAnchored)
        {
            _realtimeEstimator.ApplyYawCorrection(correction);
        }

        var proposed = _visualYawCalibration.Observe(
            visualObservation.Egomotion,
            yawSummary.Counts,
            yawSummary.Magnitude,
            movementActive,
            visualContextAiming,
            _middleMouseDown,
            settings.HipYawDegreesPerMouseCount);
        if (proposed is not { } hipYaw
            || Math.Abs(hipYaw - settings.HipYawDegreesPerMouseCount) < 1e-12)
        {
            return;
        }

        var updated = settings with { HipYawDegreesPerMouseCount = hipYaw };
        _realtimeEstimator.ApplySettings(updated);
        _ = RealtimeCalibrationStore.TrySave(
            GetRealtimeCalibrationPath(),
            updated,
            new RealtimeCalibrationSource(
                _controlSettings.MouseSensitivity,
                _controlSettings.MouseAimingSensitivity));
    }

    private void ResetPrecisionSession()
    {
        _precisionYaw.Reset();
        _precisionCalibration.Reset();
        _precisionResiduals.Reset();
        _precisionV2DriftAudit.Reset();
        _visualYawCalibration.Reset();
        ResetPrecisionV2();
        _precisionSegmentClean = true; _precisionTicks = _precisionFallbacks = _precisionGaps = 0;
        _precisionLastFallback = "aucun";
    }
    private void DisposePrecision() { _precisionUiTimer.Stop(); _precisionUiTimer.Dispose(); DisposePrecisionV2(); }
}
