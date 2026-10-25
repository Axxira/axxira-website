using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>
/// UI and lifecycle wiring for the CORTEX navigation supervisor. The existing
/// estimator and native F8 anchor remain authoritative; disabling this mode
/// restores the pre-CORTEX adaptive F8 policy without disrupting the map.
/// </summary>
internal sealed partial class OverlayForm
{
    private readonly CortexNavigationEngine _cortex = new();
    private readonly ToolStripMenuItem _cortexMenu = new(
        "CORTEX V2 — mémoire visuelle vérifiée (expérimental)")
    {
        Checked = true,
        CheckOnClick = true,
    };

    private void InitializeCortex()
    {
        _trayIcon.ContextMenuStrip?.Items.Insert(6, _cortexMenu);
        _cortexMenu.Click += (_, _) =>
        {
            _cortex.Reset();
            _trayIcon.ShowBalloonTip(3200,
                "RaidPilot CORTEX",
                _cortexMenu.Checked
                    ? "CORTEX V2 actif. Deux repères différents et corroborés sont requis pour le recalage le plus discret. F8 initial nécessaire."
                    : "CORTEX désactivé. Politique de recalage précédente conservée.",
                ToolTipIcon.Info);
        };
    }

    private void CortexObservePose(
        long now,
        RealtimePose pose,
        bool movementCommanded,
        RealtimeMotionEvidence visualEvidence,
        bool correctionAppliedThisTick)
    {
        if (!_cortexMenu.Checked) return;
        _cortex.ObservePose(now, _currentMap, _raidGeneration,
            _lastForegroundWindow, pose, movementCommanded,
            visualEvidence, correctionAppliedThisTick);
    }

    private void CortexObserveAppliedVisual(
        long now,
        VisualRelocalizationDecision applied,
        bool fromCollectiveAtlas,
        long? lowLandmarkId,
        long? superLandmarkId)
    {
        if (!_cortexMenu.Checked) return;
        _cortex.NoteAppliedVisual(now, _currentMap, _raidGeneration,
            _lastForegroundWindow, applied, fromCollectiveAtlas,
            lowLandmarkId, superLandmarkId);
    }

    private void CortexObserveNativeF8(
        long now,
        bool comparable,
        PrecisionDriftSample? measured)
    {
        if (!_cortexMenu.Checked) return;
        _cortex.NoteNativeF8(now, _currentMap, _raidGeneration,
            _lastForegroundWindow, comparable ? measured : null);
    }

    private CortexNavigationAssessment CortexAssess(
        long now,
        RealtimePose pose) =>
        _cortex.Assess(now, _currentMap, _raidGeneration,
            _lastForegroundWindow, pose, _visualRelocalization.Health);

    private string CortexDiagnostic()
    {
        if (!_cortexMenu.Checked) return "CORTEX désactivé — stratégie précédente";
        return _realtimeEstimator.CurrentPose is { } pose
            ? _cortex.Diagnostic(CortexAssess(Stopwatch.GetTimestamp(), pose))
            : "CORTEX en attente du premier F8 exact";
    }
}
