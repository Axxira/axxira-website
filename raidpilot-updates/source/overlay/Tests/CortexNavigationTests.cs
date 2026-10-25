using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class CortexNavigationTests
{
    internal const int TestCount = 23;

    private const string Map = "customs";
    private static readonly nint Window = (nint)11;
    private static readonly long T0 = 10L * Stopwatch.Frequency;

    private static long At(double seconds) =>
        T0 + (long)Math.Round(seconds * Stopwatch.Frequency);

    private static RealtimePose Pose(
        double x = 0, double uncertainty = 0.8,
        double yawUncertainty = 2.0) =>
        new(x, 0, 0, 0, uncertainty, yawUncertainty);

    private static VisualRelocalizationDecision Consensus(
        double confidence = 0.96,
        double residual = 0.6,
        VisualRelocalizationSource source = VisualRelocalizationSource.Consensus) =>
        new(VisualCorrectionMode.Medium, source,
            new VisualKeyframePose(0, 0, 0, 0),
            confidence, residual, 0.3, 3,
            0.45, 0.40, 1.0, 5.0, false,
            LocalizationHealthState.Green, "synthetic");

    private static PrecisionDriftSample F8(
        double error, double uncertainty = 0.8, double yaw = 1,
        double seconds = 8) =>
        new(true, error, yaw, seconds, uncertainty);

    private static CortexNavigationEngine Anchored()
    {
        var engine = new CortexNavigationEngine();
        engine.NoteNativeF8(At(0), Map, 1, Window, null);
        engine.ObservePose(At(0.25), Map, 1, Window, Pose(),
            movementCommanded: true, RealtimeMotionEvidence.Moving);
        return engine;
    }

    private static void Observe(CortexNavigationEngine engine, double at,
        bool movement = true,
        RealtimeMotionEvidence evidence = RealtimeMotionEvidence.Moving,
        RealtimePose? pose = null,
        string map = Map, int raid = 1, nint? window = null) =>
        engine.ObservePose(At(at), map, raid, window ?? Window,
            pose ?? Pose(), movement, evidence);

    private static CortexNavigationAssessment Assess(
        CortexNavigationEngine engine, double at, RealtimePose? pose = null,
        string map = Map, int raid = 1, nint? window = null) =>
        engine.Assess(At(at), map, raid, window ?? Window,
            pose ?? Pose(), LocalizationHealthState.Green);

    private static bool Visual(CortexNavigationEngine engine, double at,
        bool shared = false,
        VisualRelocalizationDecision? correction = null,
        string map = Map, int raid = 1, nint? window = null,
        long? lowId = 1, long? superId = 1,
        double landmarkX = 0, double landmarkY = 0) =>
        engine.NoteAppliedVisual(At(at), map, raid, window ?? Window,
            correction ?? (Consensus() with
            {
                TargetPose = new VisualKeyframePose(landmarkX, landmarkY, 0, 0)
            }),
            shared, lowId, superId);

    internal static void RunAll(ICollection<string> failures)
    {
        Run("CORTEX attend un F8 natif, jamais un GPS inventé", () =>
        {
            var e = new CortexNavigationEngine();
            Observe(e, 1);
            Require(Assess(e, 1.05).Quality == CortexTrackingQuality.WaitingForF8);
            Require(!Visual(e, 1.1));
        }, failures);

        Run("CORTEX ne confond pas temps F8 natif et recalage visuel", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.55));
            Observe(e, 0.8);
            Require(e.SecondsSinceNativeF8(At(5), Map, 1, Window) >= 4.99);
            Require(e.SecondsSinceNativeF8(At(5), "woods", 1, Window) == 0);
            Require(e.SecondsSinceNativeF8(At(5), Map, 2, Window) == 0);
            Require(e.SecondsSinceNativeF8(At(5), Map, 1, (nint)12) == 0);
        }, failures);

        Run("CORTEX conservateur après F8 sans reconnaissance visuelle", () =>
        {
            var e = Anchored();
            Require(!Assess(e, 0.30).AllowExtendedF8);
            Require(Assess(e, 0.30).Quality == CortexTrackingQuality.Predicted);
        }, failures);

        Run("CORTEX autorise extension modérée après preuve locale appliquée", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.55));
            Observe(e, 0.70);
            var a = Assess(e, 0.72);
            Require(a.AllowExtendedF8 && !a.AllowStrongExtension);
            Require(a.Quality == CortexTrackingQuality.VisuallySupported);
        }, failures);

        Run("CORTEX demande deux observations distinctes pour extension forte", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.50));
            Require(!Visual(e, 0.61));
            Observe(e, 0.78);
            Require(Visual(e, 1.0, lowId: 2, superId: 2, landmarkX: 6));
            Observe(e, 1.1);
            Require(Assess(e, 1.15).AllowStrongExtension);
            Require(e.AcceptedVisualCorrections == 2);
            Require(e.RejectedVisualCorrections == 1);
        }, failures);

        Run("CORTEX refuse repère unique et atlas collectif non vérifié", () =>
        {
            var e = Anchored();
            Require(!Visual(e, 0.50, correction: Consensus(
                source: VisualRelocalizationSource.SuperKeyframe)));
            Require(!Visual(e, 0.70, shared: true));
            Require(!Assess(e, 0.72).AllowExtendedF8);
        }, failures);

        Run("CORTEX rejette une correction visuelle sans application", () =>
        {
            var e = Anchored();
            var invalid = Consensus() with { Mode = VisualCorrectionMode.None };
            Require(!Visual(e, 0.51, correction: invalid));
            Require(!Assess(e, 0.55).AllowExtendedF8);
        }, failures);

        Run("CORTEX refuse trames dupliquées et hors ordre", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.51));
            Require(!Visual(e, 0.51));
            Require(!Visual(e, 0.40));
            Observe(e, 0.8);
            Require(!Assess(e, 0.85).AllowStrongExtension);
        }, failures);

        Run("CORTEX refuse extension forte pour même repère revisité", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.5));
            Observe(e, 0.7);
            Require(Visual(e, 1.0));
            Observe(e, 1.1);
            var assessment = Assess(e, 1.15);
            Require(assessment.AllowExtendedF8 && !assessment.AllowStrongExtension);
            Require(e.RememberedLandmarks == 1);
        }, failures);

        Run("CORTEX refuse extension forte pour deux repères adjacents", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.5));
            Observe(e, 0.7);
            Require(Visual(e, 1.0, lowId: 2, superId: 2, landmarkX: 0.4));
            Observe(e, 1.1);
            Require(!Assess(e, 1.15).AllowStrongExtension);
            Require(e.RememberedLandmarks == 2);
        }, failures);

        Run("CORTEX refuse correspondance sans les deux identifiants locaux", () =>
        {
            var e = Anchored();
            Require(!Visual(e, 0.6, lowId: null));
            Require(!Visual(e, 0.9, superId: null));
            Require(!Assess(e, 0.95).AllowExtendedF8);
        }, failures);

        Run("CORTEX rétrograde après une identité visuelle contradictoire", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.50));
            Observe(e, 0.70);
            Require(!Visual(e, 1.00, correction: Consensus() with
            {
                TargetPose = new VisualKeyframePose(35, 0, 0, 0)
            }));
            Observe(e, 1.10);
            Require(Assess(e, 1.15).Quality == CortexTrackingQuality.Degraded);
            Require(e.LandmarkIdentityConflicts == 1);
        }, failures);

        Run("CORTEX écarte un saut dû à un vrai recalage visuel", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.5));
            e.ObservePose(At(0.60), Map, 1, Window, Pose(x: 25),
                true, RealtimeMotionEvidence.Moving,
                correctionAppliedThisTick: true);
            Require(Assess(e, 0.65, pose: Pose(x: 25)).Quality
                == CortexTrackingQuality.VisuallySupported);
        }, failures);

        Run("CORTEX efface la mémoire après chaque F8 réel", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.50));
            Require(e.RememberedLandmarks == 1);
            e.NoteNativeF8(At(4), Map, 1, Window, null);
            Require(e.RememberedLandmarks == 0);
            Observe(e, 4.25);
            Require(!Assess(e, 4.3).AllowExtendedF8);
        }, failures);

        Run("CORTEX expire une preuve silencieuse non renouvelée", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.51));
            Observe(e, 6.5);
            Require(!Assess(e, 6.55).AllowExtendedF8);
        }, failures);

        Run("CORTEX invalide le suivi lors d'une contradiction mur/mouvement", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.52));
            Observe(e, 0.65, evidence: RealtimeMotionEvidence.Stationary);
            Require(Assess(e, 0.70).Quality == CortexTrackingQuality.Degraded);
        }, failures);

        Run("CORTEX détecte saut impossible entre deux poses", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.52));
            Observe(e, 0.65, pose: Pose(x: 55));
            Require(Assess(e, 0.70, pose: Pose(x: 55)).Quality
                == CortexTrackingQuality.Degraded);
        }, failures);

        Run("CORTEX refuse une pose non finie", () =>
        {
            var e = Anchored();
            Observe(e, 0.6, pose: Pose(uncertainty: double.NaN));
            Require(e.RejectedPoseObservations == 1);
            Require(Assess(e, 0.65).Quality == CortexTrackingQuality.Degraded);
        }, failures);

        Run("CORTEX apprend les erreurs F8 et bloque une fausse précision", () =>
        {
            var e = Anchored();
            e.NoteNativeF8(At(8), Map, 1, Window, F8(8.0, uncertainty: 0.8));
            Observe(e, 8.25);
            Require(Visual(e, 8.50));
            Observe(e, 8.6);
            var a = Assess(e, 8.65);
            Require(a.Quality == CortexTrackingQuality.Degraded
                && !a.AllowExtendedF8);
            Require(e.SuspiciousF8Anchors == 1
                && e.ComparableF8Anchors == 1);
        }, failures);

        Run("CORTEX restaure sa confiance seulement après délai et deux bons F8", () =>
        {
            var e = Anchored();
            e.NoteNativeF8(At(8), Map, 1, Window, F8(8));
            e.NoteNativeF8(At(44), Map, 1, Window, F8(0.3));
            e.NoteNativeF8(At(48), Map, 1, Window, F8(0.4));
            Observe(e, 48.2);
            Require(Visual(e, 48.5));
            Observe(e, 48.65);
            Require(Assess(e, 48.7).AllowExtendedF8);
            Require(e.ComparableF8Anchors == 3);
        }, failures);

        Run("CORTEX accepte une grande erreur attendue par l'incertitude", () =>
        {
            var e = Anchored();
            e.NoteNativeF8(At(8), Map, 1, Window, F8(6, uncertainty: 4));
            Require(e.SuspiciousF8Anchors == 0);
            Require(e.MedianF8ErrorMeters is 6);
        }, failures);

        Run("CORTEX isole cartes, fenêtres, raids et F8 antérieurs", () =>
        {
            var e = Anchored();
            Require(Visual(e, 0.6));
            Observe(e, 0.75, map: "woods");
            Require(!Assess(e, 0.8, map: "woods").AllowExtendedF8);
            e.NoteNativeF8(At(1.0), "woods", 2, (nint)12, null);
            Observe(e, 1.2, map: "woods", raid: 2, window: (nint)12);
            Require(!Visual(e, 1.4, map: "woods", raid: 2, window: Window));
            e.NoteNativeF8(At(0.9), "woods", 2, (nint)12, F8(12));
            Require(e.SuspiciousF8Anchors == 0);
        }, failures);

        Run("CORTEX borné en mémoire, diagnostic sans coordonnées", () =>
        {
            var e = Anchored();
            for (var i = 1; i <= 30; i++)
                e.NoteNativeF8(At(i * 8), Map, 1, Window, F8(i * 0.1));
            Require(e.ComparableF8Anchors == 30);
            Require(e.MedianF8ErrorMeters is { } median
                && median > 2.3 && median < 2.6);
            Observe(e, 240.25);
            var diag = e.Diagnostic(Assess(e, 240.3));
            Require(diag.Contains("F8 comparables 30")
                && !diag.Contains("customs")
                && !diag.Contains("coordonnées"));
        }, failures);
    }

    private static void Run(
        string title, Action test, ICollection<string> failures)
    {
        try { test(); Console.WriteLine("OK  " + title); }
        catch (Exception error) { failures.Add(title + " : " + error.Message); }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion CORTEX");
    }
}
