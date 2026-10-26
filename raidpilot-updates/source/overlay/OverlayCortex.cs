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
    private readonly CortexVisualIntelligence _cortexVision = new();
    private readonly CortexSpatialReliabilityAtlas _cortexSpatialAtlas = new();
    private string? _cortexSpatialAtlasPath;
    private int _cortexSpatialAtlasUnsaved;
    private readonly ToolStripMenuItem _cortexMenu = new(
        "CORTEX V4 — mémoire visuelle et fusion (expérimental)")
    {
        Checked = true,
        CheckOnClick = true,
    };

    private void InitializeCortex()
    {
        _cortexSpatialAtlasPath =
            CortexSpatialReliabilityStore.GetPath(_squadProfile.MemberId);
        _ = CortexSpatialReliabilityStore.TryLoad(
            _cortexSpatialAtlas, _cortexSpatialAtlasPath);
        FormClosing += (_, _) => SaveCortexSpatialAtlas();
        _trayIcon.ContextMenuStrip?.Items.Insert(6, _cortexMenu);
        _cortexMenu.Click += (_, _) =>
        {
            _cortex.Reset();
            _cortexVision.Reset();
            _trayIcon.ShowBalloonTip(3200,
                "RaidPilot CORTEX",
                _cortexMenu.Checked
                    ? "CORTEX V4 actif. Reconnaissance des lieux existants, fusion visuelle prudente, mémoire locale des erreurs et F8 adaptatifs. F8 initial nécessaire."
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
        PrecisionDriftSample? measured,
        ScreenshotObservation? nativeObservation)
    {
        if (!_cortexMenu.Checked) return;
        _cortex.NoteNativeF8(now, _currentMap, _raidGeneration,
            _lastForegroundWindow, comparable ? measured : null);
        _cortexVision.OnNativeF8(_currentMap, _raidGeneration,
            _lastForegroundWindow);
        if (comparable && measured is { } sample
            && nativeObservation is { } anchor
            && _cortexSpatialAtlas.Observe(
                _currentMap, anchor.X, anchor.Y, anchor.Z,
                sample, DateTimeOffset.UtcNow))
        {
            if (++_cortexSpatialAtlasUnsaved >= 8)
                SaveCortexSpatialAtlas();
        }
    }

    private CortexNavigationAssessment CortexAssess(long now, RealtimePose pose)
    {
        var assessment = _cortex.Assess(now, _currentMap, _raidGeneration,
            _lastForegroundWindow, pose, _visualRelocalization.Health);
        var maximum = _cortexSpatialAtlas.MaximumIntervalSeconds(
            _currentMap, pose.X, pose.Y, pose.Z, DateTimeOffset.UtcNow);
        if (assessment.AllowExtendedF8 && maximum <= 4)
            return assessment with
            {
                AllowExtendedF8 = false, AllowStrongExtension = false,
                Reason = "Secteur déjà associé à une dérive F8 élevée"
            };
        if (assessment.AllowStrongExtension && maximum <= 8)
            return assessment with
            {
                AllowStrongExtension = false,
                Reason = "Mémoire spatiale : 12 s déconseillées ici"
            };
        return assessment;
    }

    private VisualRelocalizationDecision? CortexFilterVisual(
        VisualRelocalizationDecision decision, RealtimePose estimate,
        long now, bool fromSharedAtlas, long? lowId, long? superId)
    {
        if (!_cortexMenu.Checked) return decision;
        return _cortexVision.Filter(decision, estimate, _currentMap,
            _raidGeneration, _lastForegroundWindow, now,
            fromSharedAtlas, lowId, superId);
    }

    private void CortexNoteVisualApplied()
    {
        if (_cortexMenu.Checked) _cortexVision.NoteApplied();
    }

    private void SaveCortexSpatialAtlas()
    {
        if (_cortexSpatialAtlasPath is { } path
            && CortexSpatialReliabilityStore.TrySave(
                _cortexSpatialAtlas, path))
            _cortexSpatialAtlasUnsaved = 0;
    }

    private string CortexDiagnostic()
    {
        if (!_cortexMenu.Checked) return "CORTEX désactivé — stratégie précédente";
        return _realtimeEstimator.CurrentPose is { } pose
            ? _cortex.Diagnostic(CortexAssess(Stopwatch.GetTimestamp(), pose))
                + "\n" + _cortexVision.Diagnostic
                + $"\nAtlas local : {_cortexSpatialAtlas.Count} secteurs"
                + $"; observations apprises {_cortexSpatialAtlas.Observations}"
                + $"\nRepères visuels persistants : "
                + _visualKeyframeRecognizer.KeyframeCountForMap(_currentMap)
                + " basse résolution / "
                + _superKeyframeRecognizer.KeyframeCountForMap(_currentMap)
                + " haute résolution"
            : "CORTEX en attente du premier F8 exact";
    }
}
