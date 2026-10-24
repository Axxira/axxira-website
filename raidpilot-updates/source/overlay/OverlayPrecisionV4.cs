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

    private readonly ToolStripMenuItem _precisionV4ExportMenu = new(
        "Exporter rapport comparatif F8 (.json)");

    private void InitializePrecisionV4()
    {
        if (_trayIcon.ContextMenuStrip is { } menu)
        {
            menu.Items.Insert(4, _precisionV4Menu);
            menu.Items.Insert(5, _precisionV4ExportMenu);
        }
        _precisionV4ExportMenu.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Exporter les écarts de localisation F8",
                Filter = "JSON (*.json)|*.json",
                FileName = "RaidPilot-F8-Precision-Report.json",
                OverwritePrompt = true,
                RestoreDirectory = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            try
            {
                var text = PrecisionV4Report.Build(
                    _precisionV2DriftAudit.Samples,
                    _precisionV2DriftAudit.Ignored,
                    _precisionV4Fusion.Accepted,
                    _precisionV4Fusion.Rejected);
                File.WriteAllText(dialog.FileName, text,
                    new System.Text.UTF8Encoding(false));
            }
            catch (Exception error)
            {
                MessageBox.Show(this,
                    "Export impossible : " + error.Message,
                    "RaidPilot — rapport F8",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
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
            + "\nF8 en mouvement : " + (_mobileExactAnchorEnabled ? "actif" : "désactivé")
            + $"; demandes {_mobileExactAnchorRequests}; captures reçues {_mobileExactAnchorConfirmed}"
            + "\n" + _precisionV2DriftAudit.Describe()
            + "\nGéométrie : étage surveillé. Sans polygones navigables "
            + "validés, aucun mur supposé ni GPS absolu.";
    }
}
