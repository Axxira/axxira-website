using System.Diagnostics;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private readonly PrecisionV4FusionEngine _precisionV4Fusion = new();
    private readonly ToolStripMenuItem _precisionV4Menu = new(
        "Precision V4 — fusion prudente + atlas (expérimental)")
    {
        Checked = false, // Requires comparative F8 tests before becoming default.
        CheckOnClick = true,
    };

    private void InitializePrecisionV4()
    {
        if (_trayIcon.ContextMenuStrip is { } menu)
        {
            menu.Items.Insert(4, _precisionV4Menu);
        }
        _precisionV4Menu.Click += (_, _) =>
        {
            _precisionV2DriftAudit.MarkInterrupted();
            _precisionV4Fusion.Reset();
            _skipNextRealtimeCalibration = true;
            _visualKeyframeRecognizer.CancelPendingMatch();
            _superKeyframeRecognizer.CancelPendingMatch();
            _collectiveVisualKeyframeRecognizer.CancelPendingMatch();
            _collectiveSuperKeyframeRecognizer.CancelPendingMatch();
        };
    }

    private VisualRelocalizationDecision? FilterPrecisionV4(
        VisualRelocalizationDecision proposal,
        RealtimePose poseAtVisualCapture,
        long timestamp)
    {
        if (!_precisionV4Menu.Checked)
        {
            return proposal;
        }
        return _precisionV4Fusion.TryFuse(
            _currentMap, proposal, poseAtVisualCapture,
            _visualMotionGate.CaptureProvenLive, timestamp,
            out var refined)
                ? refined
                : null;
    }

    private bool CanLearnPrecisionV4()
    {
        return !_precisionV4Menu.Checked
            || PrecisionV4CalibrationSafety.MayLearn(
                _precisionV2DriftAudit.Samples);
    }

    private string PrecisionV4Diagnostic()
    {
        var status = _precisionV4Menu.Checked ? "actif (expérimental)" : "désactivé";
        return status + " · " + _precisionV4Fusion.Diagnostic
            + "\n" + _precisionV2DriftAudit.Describe()
            + "\nGéométrie : étage surveillé. Sans polygones navigables "
            + "validés, aucun mur supposé ni GPS absolu.";
    }
}
