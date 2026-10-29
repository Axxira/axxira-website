using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class CortexV4Tests
{
    internal const int TestCount = 40;
    private const string Map = "customs";
    private static readonly nint Window = (nint)11;
    private static readonly DateTimeOffset Today =
        new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly long T0 = 10 * Stopwatch.Frequency;

    private static long At(double seconds) =>
        T0 + (long)(seconds * Stopwatch.Frequency);

    private static RealtimePose Predicted(double uncertainty = 1.3,
        double x = 0, double z = 0, double yaw = 0) =>
        new(x, 0, z, yaw, uncertainty, 4);

    private static VisualRelocalizationDecision Proposed(
        double x = 0.4, bool loop = false,
        double confidence = 0.97,
        VisualRelocalizationSource source = VisualRelocalizationSource.Consensus) =>
        new(VisualCorrectionMode.Medium, source,
            new VisualKeyframePose(x, 0, 0, 0),
            confidence, Math.Abs(x), 0.1, 2.0,
            0.85, 0.80, 4.0, 15.0,
            loop, LocalizationHealthState.Green, "confirmed matcher");

    private static VisualRelocalizationDecision? Filter(
        CortexVisualIntelligence e, double at,
        VisualRelocalizationDecision proposal,
        bool shared = false, long? low = 1, long? high = 2,
        string map = Map, int raid = 1, nint? window = null,
        RealtimePose? pose = null) =>
        e.Filter(proposal, pose ?? Predicted(), map, raid, window ?? Window,
            At(at), shared, low, high);

    private static PrecisionDriftSample Drift(double error,
        double yaw = 2, double uncertainty = 0.8) =>
        new(true, error, yaw, 8.0, uncertainty);

    internal static void RunAll(ICollection<string> failures)
    {
        Run("V4 fusion conserve le capteur de mouvement et réduit le gain", () =>
        {
            var e = new CortexVisualIntelligence();
            e.OnNativeF8(Map, 1, Window);
            var proposed = Proposed();
            var result = Filter(e, 0.5, proposed);
            Require(result is { } r && r.ShouldCorrect
                && r.PositionGain <= proposed.PositionGain
                && r.YawGain <= proposed.YawGain
                && r.MaximumTranslationStepMeters <= 2);
            e.NoteApplied();
            Require(e.Accepted == 1 && e.Mode == CortexRecoveryMode.Tracking);
        }, failures);

        Run("V4 récupère une position distante uniquement après double vue", () =>
        {
            var e = new CortexVisualIntelligence();
            e.OnNativeF8(Map, 1, Window);
            var p = Proposed(x: 9, loop: true);
            Require(Filter(e, 1, p) is null);
            Require(e.Mode == CortexRecoveryMode.AwaitingVisualConsensus);
            var accepted = Filter(e, 2, p);
            Require(accepted is { } d
                && d.MaximumTranslationStepMeters <= 0.75
                && d.MaximumYawStepDegrees <= 6
                && e.VerifiedLoopClosures == 0);
            e.NoteApplied();
            Require(e.VerifiedLoopClosures == 1);
        }, failures);

        Run("V4 rejette une boucle sans consensus basse et haute résolution", () =>
        {
            var e = new CortexVisualIntelligence();
            Require(Filter(e, 1, Proposed(x: 9, loop: true), low: null) is null);
            Require(e.Mode == CortexRecoveryMode.NeedsNativeF8);
        }, failures);

        Run("V4 n'accepte pas un atlas collectif comme preuve de récupération", () =>
        {
            var e = new CortexVisualIntelligence();
            Require(Filter(e, 1, Proposed(x: 9, loop: true), shared: true) is null);
            Require(Filter(e, 2, Proposed(x: 9, loop: true), shared: true) is null);
            Require(e.VerifiedLoopClosures == 0);
        }, failures);

        Run("V4 une autre identité visuelle ne corrobore jamais une boucle", () =>
        {
            var e = new CortexVisualIntelligence();
            Require(Filter(e, 1, Proposed(x: 9, loop: true)) is null);
            Require(Filter(e, 2, Proposed(x: 9, loop: true), high: 5) is null);
            Require(e.VerifiedLoopClosures == 0);
        }, failures);

        Run("V4 repères incohérents refusés et seconde chance relancée", () =>
        {
            var e = new CortexVisualIntelligence();
            Require(Filter(e, 1, Proposed(x: 9, loop: true)) is null);
            Require(Filter(e, 1.5, Proposed(x: 13, loop: true)) is null);
            Require(Filter(e, 1.9, Proposed(x: 13, loop: true)) is not null);
            Require(e.VerifiedLoopClosures == 0);
            e.NoteApplied();
            Require(e.VerifiedLoopClosures == 1);
        }, failures);

        Run("V4 la preuve de boucle expire au bout de 3 secondes", () =>
        {
            var e = new CortexVisualIntelligence();
            Require(Filter(e, 1, Proposed(x: 9, loop: true)) is null);
            Require(Filter(e, 4.5, Proposed(x: 9, loop: true)) is null);
            Require(e.VerifiedLoopClosures == 0);
        }, failures);

        Run("V4 n'hérite pas d'une boucle d'un autre raid ou carte", () =>
        {
            var e = new CortexVisualIntelligence();
            Require(Filter(e, 1, Proposed(x: 9, loop: true)) is null);
            Require(Filter(e, 2, Proposed(x: 9, loop: true), raid: 2) is null);
            Require(Filter(e, 2.5, Proposed(x: 9, loop: true), map: "woods") is null);
            Require(e.VerifiedLoopClosures == 0);
        }, failures);

        Run("V4 le vrai F8 efface la récupération non confirmée", () =>
        {
            var e = new CortexVisualIntelligence();
            Require(Filter(e, 1, Proposed(x: 9, loop: true)) is null);
            e.OnNativeF8(Map, 1, Window);
            Require(Filter(e, 2, Proposed(x: 9, loop: true)) is null);
            Require(e.VerifiedLoopClosures == 0);
        }, failures);

        Run("V4 refuse un saut ou une entrée non finie", () =>
        {
            var e = new CortexVisualIntelligence();
            Require(Filter(e, 1, Proposed(x: 100)) is null);
            Require(Filter(e, 2, Proposed(x: 1), pose: Predicted(double.NaN))
                is null);
        }, failures);

        Run("V4 conserve le retour F8 si navigation incertaine", () =>
        {
            var e = new CortexVisualIntelligence();
            e.OnNativeF8(Map, 1, Window);
            e.OnTrackingUncertain();
            Require(e.Mode == CortexRecoveryMode.NeedsNativeF8);
            e.OnNativeF8(Map, 1, Window);
            Require(e.Mode == CortexRecoveryMode.Tracking);
        }, failures);

        Run("V4 mémoire spatiale bloque les secteurs aux erreurs répétées", () =>
        {
            var m = new CortexSpatialReliabilityAtlas();
            for (var i = 0; i < 4; i++)
                Require(m.Observe(Map, 65, 4, -12,
                    Drift(i < 2 ? 4.0 : 0.2), Today));
            Require(m.MaximumIntervalSeconds(Map, 65, 4, -12, Today) == 4);
            Require(m.MaximumIntervalSeconds(Map, 65, 4, -80, Today) == 12);
            Require(m.MaximumIntervalSeconds("woods", 65, 4, -12, Today) == 12);
        }, failures);

        Run("V4 une seule erreur crée un plafond 8 s non permanent", () =>
        {
            var m = new CortexSpatialReliabilityAtlas();
            for (var i = 0; i < 4; i++)
                m.Observe(Map, 5, 0, 5, Drift(i == 0 ? 4 : 0.2), Today);
            Require(m.MaximumIntervalSeconds(Map, 5, 0, 5, Today) == 8);
            Require(m.MaximumIntervalSeconds(Map, 5, 0, 5,
                Today.AddDays(31)) == 12);
        }, failures);

        Run("V4 mémoire respecte la verticalité des étages", () =>
        {
            var m = new CortexSpatialReliabilityAtlas();
            for (var i = 0; i < 4; i++)
                m.Observe(Map, 5, 0, 5, Drift(4), Today);
            Require(m.MaximumIntervalSeconds(Map, 5, 10, 5, Today) == 12);
        }, failures);

        Run("V4 import persistant strict n'accepte pas les doublons", () =>
        {
            var m = new CortexSpatialReliabilityAtlas();
            for (var i = 0; i < 4; i++)
                m.Observe(Map, 5, 0, 5, Drift(4), Today);
            var snap = m.Export(Today);
            var newMemory = new CortexSpatialReliabilityAtlas();
            Require(newMemory.Import(snap, Today)
                && newMemory.MaximumIntervalSeconds(Map, 5, 0, 5, Today) == 4);
            var bad = new CortexSpatialReliabilityAtlas.Snapshot(1,
                [snap.Cells[0], snap.Cells[0]]);
            Require(!newMemory.Import(bad, Today)
                && newMemory.MaximumIntervalSeconds(Map, 5, 0, 5, Today) == 4);
        }, failures);

        Run("V4 cache peut être restauré sans conserver les images", () =>
        {
            var path = Path.Combine(Path.GetTempPath(),
                "cortex-v4-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var a = new CortexSpatialReliabilityAtlas();
                for (var i = 0; i < 4; i++)
                    a.Observe(Map, 5, 0, 5, Drift(4), Today);
                Require(CortexSpatialReliabilityStore.TrySave(a, path, Today));
                var b = new CortexSpatialReliabilityAtlas();
                Require(CortexSpatialReliabilityStore.TryLoad(b, path, Today));
                Require(b.MaximumIntervalSeconds(Map, 5, 0, 5, Today) == 4);
                var text = File.ReadAllText(path);
                Require(!text.Contains("image", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("screenshot", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }, failures);

        Run("V4 mémoire est bornée et ignore les observations impossibles", () =>
        {
            var m = new CortexSpatialReliabilityAtlas();
            Require(!m.Observe(Map, double.NaN, 0, 0, Drift(4), Today));
            Require(!m.Observe(Map, 0, 0, 0, Drift(double.NaN), Today));
            for (var i = 0; i < 560; i++)
                m.Observe(Map, (i % 280) * CortexSpatialReliabilityAtlas.CellSizeMeters,
                    0, (i / 280) * CortexSpatialReliabilityAtlas.CellSizeMeters,
                    Drift(0.2), Today.AddSeconds(i));
            Require(m.Count <= CortexSpatialReliabilityAtlas.MaximumCells);
        }, failures);

        Run("V4 déclenche la récupération F8 si immobile et incertain", () =>
        {
            Require(CortexRecoveryPolicy.ShouldRequestNativeF8(
                true, true, true, CortexRecoveryMode.NeedsNativeF8,
                2.5, 1.0, 12.5, 9.0));
        }, failures);

        Run("V4 n'interrompt pas une action ou un suivi fiable", () =>
        {
            Require(!CortexRecoveryPolicy.ShouldRequestNativeF8(
                true, true, false, CortexRecoveryMode.NeedsNativeF8,
                2.5, 1.0, 12.5, 9.0));
            Require(!CortexRecoveryPolicy.ShouldRequestNativeF8(
                true, true, true, CortexRecoveryMode.Tracking,
                2.5, 1.0, 12.5, 9.0));
            Require(!CortexRecoveryPolicy.ShouldRequestNativeF8(
                true, true, true, CortexRecoveryMode.NeedsNativeF8,
                1.0, 1.0, 12.5, 9.0));
        }, failures);

        Run("V4 le mode désactivé et les délais protègent la capture", () =>
        {
            Require(!CortexRecoveryPolicy.ShouldRequestNativeF8(
                false, true, true, CortexRecoveryMode.NeedsNativeF8,
                3.0, 1.0, 12.5, 9.0));
            Require(!CortexRecoveryPolicy.ShouldRequestNativeF8(
                true, false, true, CortexRecoveryMode.NeedsNativeF8,
                3.0, 1.0, 12.5, 9.0));
            Require(!CortexRecoveryPolicy.ShouldRequestNativeF8(
                true, true, true, CortexRecoveryMode.NeedsNativeF8,
                3.0, 1.0, 5.0, 9.0));
            Require(!CortexRecoveryPolicy.ShouldRequestNativeF8(
                true, true, true, CortexRecoveryMode.NeedsNativeF8,
                3.0, 0.2, 12.5, 9.0));
        }, failures);

        Run("V4 rapport exportable ne contient ni carte ni coordonnées", () =>
        {
            var report = CortexV4Report.Build([Drift(0.7)],
                new CortexNavigationEngine(),
                new CortexVisualIntelligence(),
                new CortexSpatialReliabilityAtlas(), true);
            Require(report.Contains("\"CORTEX V4\"")
                && report.Contains("\"measuredP90ErrorMeters\"")
                && report.Contains("\"localCoarseRiskSectors\"")
                && !report.Contains("customs")
                && !report.Contains("screenshotPath"));
        }, failures);

        Run("V4.1 boucle refuse deux trames visuelles quasi simultanées", () =>
        {
            var e = new CortexVisualIntelligence();
            e.OnNativeF8(Map, 1, Window);
            var p = Proposed(x: 9, loop: true);
            Require(Filter(e, 1, p) is null);
            Require(Filter(e, 1.05, p) is null);
            Require(e.HasPendingLoopEvidence);
            Require(Filter(e, 1.30, p) is not null);
            Require(e.VerifiedLoopClosures == 0);
            e.NoteApplied();
            Require(e.VerifiedLoopClosures == 1);
        }, failures);

        Run("V4.1 la boucle n'est comptée que si la correction est appliquée", () =>
        {
            var e = new CortexVisualIntelligence();
            e.OnNativeF8(Map, 1, Window);
            var p = Proposed(x: 9, loop: true);
            Require(Filter(e, 1, p) is null);
            Require(Filter(e, 1.5, p) is not null);
            Require(e.VerifiedLoopClosures == 0);
            e.OnNativeF8(Map, 1, Window);
            Require(e.VerifiedLoopClosures == 0
                && e.Mode == CortexRecoveryMode.Tracking);
        }, failures);

        Run("V4.1 récupération confirmée avance par étapes mesurées", () =>
        {
            var e = new CortexVisualIntelligence();
            e.OnNativeF8(Map, 1, Window);
            var p = Proposed(x: 9, loop: true);
            Require(Filter(e, 1, p) is null);
            Require(Filter(e, 1.5, p) is { } accepted
                && accepted.MaximumTranslationStepMeters <= 0.75);
            e.NoteApplied();
            for (var k = 0; k < 4; k++)
            {
                Require(Filter(e, 2 + k * 0.5, p) is { } next
                    && next.MaximumTranslationStepMeters <= 0.75);
                e.NoteApplied();
            }
            Require(e.VerifiedLoopClosures == 1
                && e.VerifiedRecoverySteps == 5
                && e.Mode == CortexRecoveryMode.Recovering);
        }, failures);

        Run("V4.1 récupération limitée demande F8 après six étapes", () =>
        {
            var e = new CortexVisualIntelligence();
            e.OnNativeF8(Map, 1, Window);
            var p = Proposed(x: 9, loop: true);
            Require(Filter(e, 1, p) is null);
            Require(Filter(e, 1.5, p) is not null);
            e.NoteApplied();
            for (var k = 0; k < 5; k++)
            {
                Require(Filter(e, 2 + k * 0.5, p) is not null);
                e.NoteApplied();
            }
            Require(e.VerifiedRecoverySteps == 6
                && e.Mode == CortexRecoveryMode.NeedsNativeF8);
            Require(Filter(e, 4.75, p) is null);
            e.OnNativeF8(Map, 1, Window);
            Require(e.Mode == CortexRecoveryMode.Tracking);
        }, failures);

        Run("V4.1 répétitions trop rapprochées ne détruisent pas le suivi", () =>
        {
            var e = new CortexVisualIntelligence();
            e.OnNativeF8(Map, 1, Window);
            var p = Proposed(x: 9, loop: true);
            Require(Filter(e, 1, p) is null);
            Require(Filter(e, 1.5, p) is not null);
            e.NoteApplied();
            Require(Filter(e, 1.51, p) is null);
            Require(e.Mode == CortexRecoveryMode.Recovering);
            Require(Filter(e, 1.90, p) is not null);
            e.NoteApplied();
            Require(e.VerifiedRecoverySteps == 2);
        }, failures);

        Run("V4.1 preuve d'un nouveau lieu invalide une boucle précédente", () =>
        {
            var e = new CortexVisualIntelligence();
            e.OnNativeF8(Map, 1, Window);
            var p = Proposed(x: 9, loop: true);
            Require(Filter(e, 1, p) is null);
            Require(Filter(e, 1.5, p) is not null);
            e.NoteApplied();
            Require(Filter(e, 2, p, high: 9) is null);
            Require(e.Mode == CortexRecoveryMode.NeedsNativeF8);
        }, failures);

        Run("V4.1 retour F8 supprime la mémoire de récupération", () =>
        {
            var e = new CortexVisualIntelligence();
            e.OnNativeF8(Map, 1, Window);
            var p = Proposed(x: 9, loop: true);
            Require(Filter(e, 1, p) is null);
            Require(Filter(e, 1.5, p) is not null);
            e.NoteApplied();
            e.OnNativeF8(Map, 1, Window);
            Require(Filter(e, 2, p) is null
                && e.Mode == CortexRecoveryMode.AwaitingVisualConsensus);
        }, failures);

        Run("V4.1 retour visuel/F8 apprend seulement les F8 comparables", () =>
        {
            var g = new CortexVisualFeedbackGate();
            g.OnNativeF8(At(0), Map, 1, Window, null);
            g.OnVisualApplied(At(1), Map, 1, Window, Proposed(), false);
            g.OnNativeF8(At(8), Map, 1, Window, Drift(0.1));
            Require(g.SampleCount == 1 && !g.Calibrated);
            g.OnVisualApplied(At(9), Map, 1, Window, Proposed(), true);
            g.OnNativeF8(At(16), Map, 1, Window, Drift(4.0));
            Require(g.SampleCount == 1);
            g.OnVisualApplied(At(17), Map, 1, Window, Proposed(), false);
            g.OnNativeF8(At(24), Map, 1, Window, null);
            Require(g.SampleCount == 1 && g.Ignored == 1);
        }, failures);

        Run("V4.1 F8 invalides, doubles et désynchronisés sont ignorés", () =>
        {
            var g = new CortexVisualFeedbackGate();
            g.OnNativeF8(At(0), Map, 1, Window, null);
            g.OnVisualApplied(At(1), Map, 1, Window, Proposed(), false);
            g.OnNativeF8(At(8), Map, 1, Window, Drift(0.4));
            g.OnNativeF8(At(8), Map, 1, Window, Drift(5));
            g.OnVisualApplied(At(9), Map, 1, Window, Proposed(), false);
            g.OnNativeF8(At(16), Map, 1, Window,
                new PrecisionDriftSample(true, 0.4, 1, 19, 0.8));
            Require(g.SampleCount == 1 && g.Comparisons == 1
                && g.Ignored == 1);
        }, failures);

        Run("V4.1 confiance F8 faible borne les captures à 4 secondes", () =>
        {
            var g = new CortexVisualFeedbackGate();
            g.OnNativeF8(At(0), Map, 1, Window, null);
            for (var i = 0; i < 4; i++)
            {
                g.OnVisualApplied(At(i * 8 + 1), Map, 1, Window,
                    Proposed(), false);
                g.OnNativeF8(At((i + 1) * 8), Map, 1, Window,
                    Drift(i < 2 ? 4 : 0.2));
            }
            Require(g.Calibrated && g.SampleCount == 4
                && g.MaximumIntervalSecondsAllowed == 4
                && g.Diagnostic.Contains("plafond 4 s"));
        }, failures);

        Run("V4.1 bonnes corrections successives rétablissent le plafond 12 s", () =>
        {
            var g = new CortexVisualFeedbackGate();
            g.OnNativeF8(At(0), Map, 1, Window, null);
            for (var i = 0; i < 28; i++)
            {
                g.OnVisualApplied(At(i * 8 + 1), Map, 1, Window,
                    Proposed(), false);
                g.OnNativeF8(At((i + 1) * 8), Map, 1, Window,
                    Drift(i < 4 ? 4 : 0.1));
            }
            Require(g.SampleCount == CortexVisualFeedbackGate.Capacity
                && g.MaximumIntervalSecondsAllowed == 12);
        }, failures);

        Run("V4.1 rétroaction visuelle isolée entre parties et fenêtres", () =>
        {
            var g = new CortexVisualFeedbackGate();
            g.OnNativeF8(At(0), Map, 1, Window, null);
            for (var i = 0; i < 4; i++)
            {
                g.OnVisualApplied(At(i * 8 + 1), Map, 1, Window,
                    Proposed(), false);
                g.OnNativeF8(At((i + 1) * 8), Map, 1, Window, Drift(4));
            }
            Require(g.MaximumIntervalSecondsAllowed == 4);
            g.OnNativeF8(At(40), Map, 2, Window, null);
            Require(g.SampleCount == 0
                && g.MaximumIntervalSecondsAllowed == 12);
            g.OnVisualApplied(At(41), Map, 2, (nint)99, Proposed(), false);
            Require(g.SampleCount == 0);
        }, failures);
        Run("F8 journal distingue PNG bruts et véritables ancrages", () =>
        {
            var j = new CortexF8EvidenceJournal();
            j.ObserveRawScreenshotNotification(Today);
            j.ObserveRawScreenshotNotification(Today.AddSeconds(1));
            var snapshot = j.Export(Today.AddSeconds(2));
            Require(snapshot.RawScreenshotNotifications == 2
                && snapshot.Events.Count == 0);
            var restored = new CortexF8EvidenceJournal();
            Require(restored.Import(snapshot, Today.AddSeconds(2)));
            var summary = restored.Summarize(Today.AddSeconds(2));
            Require(summary.RetainedF8Events == 0
                && summary.RawScreenshotNotifications == 2
                && summary.Comparable == 0);
        }, failures);

        Run("F8 journal conserve le premier ancrage et les causes de rejet", () =>
        {
            var j = new CortexF8EvidenceJournal();
            j.Observe(Today, true, PrecisionF8Outcome.FirstAnchor,
                true, false, null);
            j.Observe(Today.AddSeconds(8), true,
                PrecisionF8Outcome.MissingPrediction, true, false, null);
            j.Observe(Today.AddSeconds(16), true,
                PrecisionF8Outcome.Comparable, true, true, Drift(0.4));
            var s = j.Summarize(Today.AddSeconds(17));
            Require(s.RetainedF8Events == 3 && s.WithCortexV4Enabled == 3
                && s.WithPredictedPose == 1
                && s.ComparableWithCortexV4 == 1
                && s.Outcomes[nameof(PrecisionF8Outcome.FirstAnchor)] == 1
                && s.Outcomes[nameof(PrecisionF8Outcome.MissingPrediction)] == 1
                && s.MeasuredP90ErrorMeters == 0.4);
        }, failures);

        Run("F8 journal survit au chargement et ne stocke pas la position", () =>
        {
            var path = Path.Combine(Path.GetTempPath(),
                "cortex-f8-evidence-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var j = new CortexF8EvidenceJournal();
                j.Observe(Today, false, PrecisionF8Outcome.FirstAnchor,
                    true, false, null);
                j.Observe(Today.AddSeconds(8), true,
                    PrecisionF8Outcome.Comparable, true, true, Drift(.3));
                Require(CortexF8EvidenceStore.TrySave(j, path, Today.AddMinutes(1)));
                var reloaded = new CortexF8EvidenceJournal();
                Require(CortexF8EvidenceStore.TryLoad(reloaded, path,
                    Today.AddMinutes(1)) && reloaded.Count == 2);
                var bytes = File.ReadAllText(path);
                Require(!bytes.Contains("Screenshot", StringComparison.OrdinalIgnoreCase)
                    && !bytes.Contains("MemberId", StringComparison.OrdinalIgnoreCase)
                    && !bytes.Contains("Map", StringComparison.OrdinalIgnoreCase)
                    && !bytes.Contains("Position", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }, failures);

        Run("F8 journal garde la raison quand le résidu est invalide", () =>
        {
            var j = new CortexF8EvidenceJournal();
            j.Observe(Today, true, PrecisionF8Outcome.Comparable,
                true, true, null);
            var s = j.Summarize(Today);
            Require(s.Comparable == 0
                && s.LastOutcome == nameof(PrecisionF8Outcome.InvalidResidual));
        }, failures);

        Run("F8 journal refuse les entrées persistées fabriquées", () =>
        {
            var j = new CortexF8EvidenceJournal();
            var invalid = new CortexF8EvidenceJournal.Snapshot(1,
                [new CortexF8EvidenceJournal.Entry(
                    Today.ToUnixTimeSeconds(), true,
                    PrecisionF8Outcome.Comparable, true, true, null, null)]);
            Require(!j.Import(invalid, Today)
                && j.Count == 0);
        }, failures);

        Run("F8 journal est borné et expire sans dégrader la navigation", () =>
        {
            var j = new CortexF8EvidenceJournal();
            for (var i = 0; i < CortexF8EvidenceJournal.MaximumEvents + 5; i++)
                j.Observe(Today.AddSeconds(i * 8), true,
                    PrecisionF8Outcome.Interrupted, true, false, null);
            Require(j.Count == CortexF8EvidenceJournal.MaximumEvents);
            var navigation = new CortexNavigationEngine();
            navigation.Reset();
            Require(j.Count == CortexF8EvidenceJournal.MaximumEvents);
            Require(j.Summarize(Today.AddDays(15)).RetainedF8Events == 0);
        }, failures);

        Run("CORTEX rapport V2 distingue session et F8 historiques", () =>
        {
            using var parsed = System.Text.Json.JsonDocument.Parse(
                CortexV4Report.Build(Array.Empty<PrecisionDriftSample>(),
                    new CortexNavigationEngine(),
                    new CortexVisualIntelligence(),
                    new CortexSpatialReliabilityAtlas(), true,
                    f8Evidence: new CortexF8EvidenceJournal()));
            var root = parsed.RootElement;
            Require(root.GetProperty("schemaVersion").GetInt32() == 2
                && root.GetProperty("nativeF8")
                    .GetProperty("comparable").GetInt64() == 0
                && root.GetProperty("capturePipeline")
                    .GetProperty("RetainedF8Events").GetInt32() == 0);
        }, failures);

    }

    private static void Run(string label, Action test,
        ICollection<string> errors)
    {
        try { test(); Console.WriteLine("OK  " + label); }
        catch (Exception ex) { errors.Add(label + ": " + ex.Message); }
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Assertion CORTEX V4");
    }
}
