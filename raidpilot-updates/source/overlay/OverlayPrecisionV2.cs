using System.Diagnostics;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private readonly PrecisionV2VisualSampler _precisionV2Vision = new();
    private readonly PrecisionV2MotionFusion _precisionV2Fusion = new();
    private readonly PrecisionDriftAudit _precisionV2DriftAudit = new();
    private readonly ToolStripMenuItem _precisionV2ToggleMenuItem = new(
        "Precision V2 — flux optique 160×90 (expérimental)")
    {
        Checked = true,
        CheckOnClick = true,
    };
    private readonly ToolStripMenuItem _precisionV3GpuMenuItem = new(
        "Capteur GPU WGC/Direct3D11 (expérimental, capture visible)")
    {
        Checked = false,
        CheckOnClick = true,
    };
    private bool _precisionV2TrackingActive;
    private long _precisionV2LastTurnAt;

    private void InitializePrecisionV2()
    {
        _precisionV2Vision.ConfigureVerticalFieldOfView(
            EftGameViewSettings.Read().VerticalFieldOfViewDegrees);
        if (_trayIcon.ContextMenuStrip is { } menu)
        {
            menu.Items.Insert(2, _precisionV2ToggleMenuItem);
            menu.Items.Insert(3, _precisionV3GpuMenuItem);
        }
        _precisionV3GpuMenuItem.Click += (_, _) =>
        {
            _precisionV2DriftAudit.MarkInterrupted();
            _precisionV2Vision.SelectGpuCapture(_precisionV3GpuMenuItem.Checked);
            _precisionV2Fusion.Reset();
        };
        _precisionV2ToggleMenuItem.Click += (_, _) =>
        {
            _precisionV2DriftAudit.MarkInterrupted();
            ResetPrecisionV2();
        };
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
        var status = !_precisionV2ToggleMenuItem.Checked
            ? "désactivé (suivi classique)"
            : $"{_precisionV2Vision.Backend}, "
                + $"captures {_precisionV2Vision.Captured}, erreurs {_precisionV2Vision.Failed}, "
                + $"arrêts fiables {_precisionV2Fusion.AcceptedStops}, "
                + $"flux fiables {_precisionV2Vision.ReliableFrames}, "
                + $"flux incertains {_precisionV2Vision.UnreliableFrames}, "
                + $"dernier calcul {_precisionV2Vision.LastProcessingMilliseconds:F0} ms, "
                + $"images GPU {_precisionV2Vision.GpuFrames}, "
                + $"replis GPU {_precisionV2Vision.GpuFallbacks}, "
                + $"formats rejetés {_precisionV2Vision.GeometryRejected}, "
                + $"trames GPU conservées {_precisionV2Vision.GpuHeldPendingPolls}, "
                + $"nouveaux repères après pause {_precisionV2Vision.GpuLongGapReprimes}\n"
                + $"Géométrie : {_precisionV2Vision.CameraGeometryDiagnostic}\n"
                + $"Arbitrage : {_precisionV2Fusion.LastDecision}";
        return status + "\nContrôle format : " + _precisionV2Vision.GeometryWarning
            + "\nCapteur Windows : " + _precisionV2Vision.GpuStatus
            + "\n\n" + _precisionV2DriftAudit.Describe();
    }

    private void DisposePrecisionV2() => _precisionV2Vision.Dispose();
}
