using System.Diagnostics;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private readonly PrecisionV2VisualSampler _precisionV2Vision = new();
    private readonly PrecisionV2MotionFusion _precisionV2Fusion = new();
    private readonly ToolStripMenuItem _precisionV2ToggleMenuItem = new(
        "Precision V2 — flux optique 160×90 (expérimental)")
    {
        Checked = true,
        CheckOnClick = true,
    };
    private bool _precisionV2TrackingActive;
    private long _precisionV2LastTurnAt;

    private void InitializePrecisionV2()
    {
        _precisionV2Vision.ConfigureVerticalFieldOfView(
            EftGameViewSettings.Read().VerticalFieldOfViewDegrees);
        if (_trayIcon.ContextMenuStrip is { } menu)
            menu.Items.Insert(2, _precisionV2ToggleMenuItem);
        _precisionV2ToggleMenuItem.Click += (_, _) => ResetPrecisionV2();
    }

    private void ResetPrecisionV2()
    {
        _precisionV2Vision.Reset();
        _precisionV2Fusion.Reset();
        _precisionV2TrackingActive = false;
        _precisionV2LastTurnAt = 0;
    }

    private RealtimeMotionEvidence FusePrecisionV2(
        long now,
        bool movementInputActive,
        bool rotationOccurred,
        bool aiming,
        RealtimeMotionEvidence baseline)
    {
        if (!_precisionV2ToggleMenuItem.Checked
            || !_realtimeEstimator.IsAnchored
            || !_tarkovIsForeground
            || !_controlTrackingActive
            || _lastForegroundWindow == nint.Zero)
        {
            if (_precisionV2TrackingActive) ResetPrecisionV2();
            return baseline;
        }

        _precisionV2TrackingActive = true;
        if (rotationOccurred || _middleMouseDown)
            _precisionV2LastTurnAt = now;
        var quietDirection = !_middleMouseDown && !aiming
            && (_precisionV2LastTurnAt == 0
                || (now - _precisionV2LastTurnAt) / (double)Stopwatch.Frequency >= 0.45);

        // Consume an already completed worker frame BEFORE integrating
        // this tick's motion; queue a fresh capture without blocking UI.
        var observation = _precisionV2Vision.GetFresh(now, _lastForegroundWindow);
        var fused = _precisionV2Fusion.Fuse(
            observation, now, _lastForegroundWindow,
            movementInputActive, quietDirection, baseline);
        _precisionV2Vision.Request(_lastForegroundWindow, now);
        return fused;
    }

    private string PrecisionV2Diagnostic()
    {
        if (!_precisionV2ToggleMenuItem.Checked)
            return "désactivé (suivi classique)";
        return $"{_precisionV2Vision.Backend}, "
            + $"captures {_precisionV2Vision.Captured}, erreurs {_precisionV2Vision.Failed}, "
            + $"arrêts fiables {_precisionV2Fusion.AcceptedStops}\n"
            + $"Arbitrage : {_precisionV2Fusion.LastDecision}";
    }

    private void DisposePrecisionV2() => _precisionV2Vision.Dispose();
}
