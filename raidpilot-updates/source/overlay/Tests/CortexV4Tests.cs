using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class CortexV4Tests
{
    internal const int TestCount = 20;
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
                && e.VerifiedLoopClosures == 1);
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
                m.Observe(Map, i * CortexSpatialReliabilityAtlas.CellSizeMeters,
                    0, 0, Drift(0.2), Today.AddSeconds(i));
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
