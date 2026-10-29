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
    private readonly CortexVisualFeedbackGate _cortexVisualFeedback = new();
    private readonly CortexNativeCadenceMemory _cortexNativeCadence = new();
    private readonly CortexF8EvidenceJournal _cortexF8Evidence = new();
    private string? _cortexF8EvidencePath;
    private bool _cortexF8EvidenceSaveFailed;
    private int _cortexF8EvidenceUnsaved;
    private string? _cortexNativeCadencePath;
    private int _cortexNativeCadenceUnsaved;
    private string? _cortexSpatialAtlasPath;
    private int _cortexSpatialAtlasUnsaved;
    private readonly ToolStripMenuItem _cortexMenu = new(
        "CORTEX V3 — apprentissage F8")
    {
        Checked = true,
        CheckOnClick = true,
    };
    private readonly ToolStripMenuItem _cortexV4Menu = new(
        "CORTEX V4.1 — fusion et récupération visuelle (test)")
    {
        Checked = false, // Explicit player opt-in until field comparison.
        CheckOnClick = true,
    };
    private readonly ToolStripMenuItem _cortexV4ReportMenu = new(
        "CORTEX V4 — exporter mesures F8 (.json)");

    private void InitializeCortex()
    {
        _cortexSpatialAtlasPath =
            CortexSpatialReliabilityStore.GetPath(_squadProfile.MemberId);
        _ = CortexSpatialReliabilityStore.TryLoad(
            _cortexSpatialAtlas, _cortexSpatialAtlasPath);
        _cortexF8EvidencePath =
            CortexF8EvidenceStore.GetPath(_squadProfile.MemberId);
        _ = CortexF8EvidenceStore.TryLoad(
            _cortexF8Evidence, _cortexF8EvidencePath);
        _cortexNativeCadencePath =
            CortexNativeCadenceStore.GetPath(_squadProfile.MemberId);
        _ = CortexNativeCadenceStore.TryLoad(
            _cortexNativeCadence, _cortexNativeCadencePath);
        FormClosing += (_, _) =>
        {
            SaveCortexSpatialAtlas();
            SaveCortexNativeCadence();
            SaveCortexF8Evidence();
        };
        _trayIcon.ContextMenuStrip?.Items.Insert(6, _cortexMenu);
        _trayIcon.ContextMenuStrip?.Items.Insert(7, _cortexV4Menu);
        _trayIcon.ContextMenuStrip?.Items.Insert(8, _cortexV4ReportMenu);
        _cortexV4ReportMenu.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Exporter les mesures CORTEX V4",
                Filter = "JSON (*.json)|*.json",
                FileName = "RaidPilot-CORTEX-V4-Report.json",
                OverwritePrompt = true, RestoreDirectory = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllText(dialog.FileName,
                    CortexV4Report.Build(_precisionV2DriftAudit.Samples,
                        _cortex, _cortexVision, _cortexSpatialAtlas,
                        _cortexV4Menu.Checked, _cortexVisualFeedback,
                        _cortexNativeCadence, _cortexF8Evidence),
                    new System.Text.UTF8Encoding(false));
            }
            catch (Exception error) when (error is IOException
                or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
            {
                MessageBox.Show(this, error.Message,
                    "Export CORTEX V4 impossible", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };
        _cortexV4Menu.Click += (_, _) =>
        {
            if (_cortexV4Menu.Checked) _cortexMenu.Checked = true;
            _cortex.Reset();
            _cortexVision.Reset();
            _cortexVisualFeedback.Reset();
            _cortexNativeCadence.ResetSession();
            _trayIcon.ShowBalloonTip(2200, "CORTEX V4",
                _cortexV4Menu.Checked
                    ? "CORTEX V4.1 actif : récupération progressive et retour F8 supervisé (test)."
                    : "V4 désactivé. CORTEX V3 reste disponible.",
                ToolTipIcon.Info);
        };
        _cortexMenu.Click += (_, _) =>
        {
            if (!_cortexMenu.Checked) _cortexV4Menu.Checked = false;
            _cortex.Reset();
            _cortexVision.Reset();
            _cortexVisualFeedback.Reset();
            _cortexNativeCadence.ResetSession();
            _trayIcon.ShowBalloonTip(3200,
                "RaidPilot CORTEX",
                _cortexMenu.Checked
                    ? "CORTEX V3 actif. Cadence F8 adaptative; V4 disponible séparément en mode test."
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
        if (_cortexV4Menu.Checked)
            _cortexVisualFeedback.OnVisualApplied(
                now, _currentMap, _raidGeneration, _lastForegroundWindow,
                applied, fromCollectiveAtlas);
    }

    private void CortexObserveRawScreenshotNotification()
    {
        _cortexF8Evidence.ObserveRawScreenshotNotification(DateTimeOffset.UtcNow);
        // Do not do synchronous disk IO for every PNG watcher notification
        // on the UI thread: save in batches, and always on FormClosing.
        if (++_cortexF8EvidenceUnsaved >= 8) SaveCortexF8Evidence();
    }

    private void CortexObserveNativeF8(
        long now,
        bool comparable,
        PrecisionDriftSample? measured,
        ScreenshotObservation? nativeObservation,
        PrecisionF8Outcome auditOutcome,
        bool captureTimeResolved,
        bool predictedPoseAvailable)
    {
        // Record the *actual native F8 event* even if CORTEX is turned off.
        // Otherwise enabling V4 midway through a raid hides the reasons for
        // missing comparisons, and restarting loses the entire diagnosis.
        _cortexF8Evidence.Observe(DateTimeOffset.UtcNow,
            _cortexV4Menu.Checked, auditOutcome, captureTimeResolved,
            predictedPoseAvailable, comparable ? measured : null);
        if (++_cortexF8EvidenceUnsaved >= 4) SaveCortexF8Evidence();
        if (!_cortexMenu.Checked) return;
        _cortex.NoteNativeF8(now, _currentMap, _raidGeneration,
            _lastForegroundWindow, comparable ? measured : null);
        if (!_cortexV4Menu.Checked) return;
        _cortexVision.OnNativeF8(_currentMap, _raidGeneration,
            _lastForegroundWindow);
        _cortexVisualFeedback.OnNativeF8(now, _currentMap,
            _raidGeneration, _lastForegroundWindow,
            comparable ? measured : null);
        if (_cortexNativeCadence.ObserveNative(
            _currentMap, _raidGeneration, _lastForegroundWindow,
            comparable ? measured : null, DateTimeOffset.UtcNow))
        {
            if (++_cortexNativeCadenceUnsaved >= 4)
                SaveCortexNativeCadence();
        }
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
        if (_cortexV4Menu.Checked
            && assessment.Quality == CortexTrackingQuality.Degraded)
            _cortexVision.OnTrackingUncertain();
        if (!_cortexV4Menu.Checked) return assessment;
        var spatialMaximum = _cortexSpatialAtlas.MaximumIntervalSeconds(
            _currentMap, pose.X, pose.Y, pose.Z, DateTimeOffset.UtcNow);
        var visualMaximum = _cortexVisualFeedback.MaximumIntervalSecondsAllowed;
        var maximum = Math.Min(spatialMaximum, visualMaximum);
        if (assessment.AllowExtendedF8 && maximum <= 4)
            return assessment with
            {
                AllowExtendedF8 = false, AllowStrongExtension = false,
                Reason = visualMaximum <= 4
                    ? "Contrôle V4 : dérives F8 après vision locale"
                    : "Secteur déjà associé à une dérive F8 élevée"
            };
        if (assessment.AllowStrongExtension && maximum <= 8)
            return assessment with
            {
                AllowStrongExtension = false,
                Reason = visualMaximum <= 8
                    ? "Contrôle V4 : fiabilité des repères limite 12 s"
                    : "Mémoire spatiale : 12 s déconseillées ici"
            };
        return assessment;
    }

    private double CortexNativeAdaptiveIntervalSeconds(
        long now, RealtimePose? pose,
        CortexNavigationAssessment assessment)
    {
        // Enabled only in the explicit CORTEX V4 test mode. On every live
        // tick the native tier is re-evaluated: loss of accuracy restores 4 s.
        if (!_cortexMenu.Checked || !_cortexV4Menu.Checked
            || pose is not { } current
            || _cortex.SecondsSinceNativeF8(
                now, _currentMap, _raidGeneration, _lastForegroundWindow) > 9)
            return MobileExactAnchorPolicy.MinimumIntervalSeconds;

        var tier = _cortexNativeCadence.EligibleNativeIntervalSeconds(
            _currentMap, _raidGeneration, _lastForegroundWindow,
            current, _visualRelocalization.Health, assessment,
            _cortex.DriftRisk, DateTimeOffset.UtcNow);

        // Existing V4 risk-atlas and visual-feedback vetoes apply to the
        // historical evidence too; one local bad sector cannot be ignored.
        var spatialLimit = _cortexSpatialAtlas.MaximumIntervalSeconds(
            _currentMap, current.X, current.Y, current.Z, DateTimeOffset.UtcNow);
        return Math.Min(tier, Math.Min(
            spatialLimit, _cortexVisualFeedback.MaximumIntervalSecondsAllowed));
    }

    private VisualRelocalizationDecision? CortexFilterVisual(
        VisualRelocalizationDecision decision, RealtimePose estimate,
        long now, bool fromSharedAtlas, long? lowId, long? superId)
    {
        if (!_cortexMenu.Checked || !_cortexV4Menu.Checked) return decision;
        return _cortexVision.Filter(decision, estimate, _currentMap,
            _raidGeneration, _lastForegroundWindow, now,
            fromSharedAtlas, lowId, superId);
    }

    private void CortexNoteVisualApplied()
    {
        if (_cortexMenu.Checked && _cortexV4Menu.Checked)
            _cortexVision.NoteApplied();
    }

    private void SaveCortexF8Evidence()
    {
        if (_cortexF8EvidencePath is not { } path) return;
        _cortexF8EvidenceSaveFailed =
            !CortexF8EvidenceStore.TrySave(_cortexF8Evidence, path);
        if (!_cortexF8EvidenceSaveFailed) _cortexF8EvidenceUnsaved = 0;
    }

    private void SaveCortexNativeCadence()
    {
        if (_cortexNativeCadencePath is { } path
            && CortexNativeCadenceStore.TrySave(
                _cortexNativeCadence, path))
            _cortexNativeCadenceUnsaved = 0;
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
                + (_cortexV4Menu.Checked
                    ? "\n" + _cortexVision.Diagnostic
                        + "\n" + _cortexVisualFeedback.Diagnostic
                        + "\n" + _cortexNativeCadence.Diagnostic(
                            _currentMap, DateTimeOffset.UtcNow)
                        + $"\nF8 natif recommandé : "
                        + CortexNativeAdaptiveIntervalSeconds(
                            Stopwatch.GetTimestamp(), pose,
                            CortexAssess(Stopwatch.GetTimestamp(), pose))
                            .ToString("F0") + " s (palier prudent)"
                    : "\nCORTEX V4 : désactivé (optionnel)")
                + $"\nAtlas local : {_cortexSpatialAtlas.Count} secteurs"
                + $"; observations apprises {_cortexSpatialAtlas.Observations}"
                + $"\nJournal F8 conservé : {_cortexF8Evidence.Count} événements"
                + (_cortexF8EvidenceSaveFailed ? " (SAUVEGARDE ÉCHOUÉE)" : "")
                + $"\nRepères visuels persistants : "
                + _visualKeyframeRecognizer.KeyframeCountForMap(_currentMap)
                + " basse résolution / "
                + _superKeyframeRecognizer.KeyframeCountForMap(_currentMap)
                + " haute résolution"
            : "CORTEX en attente du premier F8 exact"
                + $"\nJournal F8 conservé : {_cortexF8Evidence.Count} événements"
                + (_cortexF8EvidenceSaveFailed ? " (SAUVEGARDE ÉCHOUÉE)" : "");
    }
}
