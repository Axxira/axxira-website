namespace RaidPilot.Overlay.Tests;

internal static class CortexNativeCadenceTests
{
    internal const int TestCount = 11;
    private const string Map = "customs";
    private static readonly nint Window = (nint)11;
    private static readonly DateTimeOffset Today =
        new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static PrecisionDriftSample Good =>
        new(true, 0.4, 2, 8.0, 0.8);
    private static PrecisionDriftSample Bad =>
        new(true, 4.2, 19, 8.0, 0.8);
    private static CortexNavigationAssessment Healthy =>
        new(CortexTrackingQuality.Predicted, false, false, 0.8,
            "Pas de repère visuel simultané");
    private static CortexDriftRiskSnapshot Cold =>
        new(0, 12, double.NaN, double.NaN, double.NaN, double.NaN,
            "Apprentissage initial");
    private static RealtimePose Pose(double uncertainty = 0.8, double yaw = 3) =>
        new(0, 0, 0, 0, uncertainty, yaw);
    private static void Observe(CortexNativeCadenceMemory m, int samples,
        int raid = 1, string map = Map)
    {
        for (var i = 0; i < samples; i++)
            m.ObserveNative(map, raid, Window, Good, Today.AddSeconds(i * 8));
    }
    private static double Tier(CortexNativeCadenceMemory m, int raid = 1,
        RealtimePose? pose = null,
        LocalizationHealthState health = LocalizationHealthState.Green,
        CortexNavigationAssessment? assessment = null,
        CortexDriftRiskSnapshot? risk = null, string map = Map) =>
        m.EligibleNativeIntervalSeconds(map, raid, Window, pose ?? Pose(),
            health, assessment ?? Healthy, risk ?? Cold, Today.AddMinutes(4));

    internal static void RunAll(ICollection<string> errors)
    {
        Run("CORTEX F8 historique commence au palier quatre secondes", () =>
        {
            var m = new CortexNativeCadenceMemory();
            m.ObserveNative(Map, 1, Window, null, Today);
            Require(Tier(m) == 4);
            Observe(m, 5);
            Require(Tier(m) == 4);
        }, errors);

        Run("CORTEX F8 six écarts exacts faibles ouvrent seulement six secondes", () =>
        {
            var m = new CortexNativeCadenceMemory();
            Observe(m, 6);
            Require(Tier(m) == 6 && m.ComparedInRaid == 6);
        }, errors);

        Run("CORTEX F8 quatorze écarts exacts faibles autorisent huit secondes", () =>
        {
            var m = new CortexNativeCadenceMemory();
            Observe(m, 14);
            Require(Tier(m) == 8);
            Require(m.Diagnostic(Map, Today).Contains("historique fiable"));
        }, errors);

        Run("CORTEX F8 conserve le bon historique mais exige deux F8 du nouveau raid", () =>
        {
            var m = new CortexNativeCadenceMemory();
            Observe(m, 16);
            m.ObserveNative(Map, 2, Window, null, Today.AddHours(1));
            Require(Tier(m, raid: 2) == 4);
            Observe(m, 2, raid: 2);
            Require(Tier(m, raid: 2) == 6);
            Observe(m, 2, raid: 2);
            Require(Tier(m, raid: 2) == 8);
        }, errors);

        Run("CORTEX F8 refuse une promotion sans données natives comparables", () =>
        {
            var m = new CortexNativeCadenceMemory();
            Observe(m, 12);
            m.ObserveNative(Map, 1, Window, null, Today);
            Require(Tier(m) == 4 && m.NotComparableInRaid == 1);
        }, errors);

        Run("CORTEX F8 n'allonge jamais sur un F8 récent erroné", () =>
        {
            var m = new CortexNativeCadenceMemory();
            Observe(m, 14);
            m.ObserveNative(Map, 1, Window, Bad, Today);
            Require(Tier(m) == 4);
            Require(m.Diagnostic(Map, Today).Contains("forte dérive 1"));
        }, errors);

        Run("CORTEX F8 conserve les protections incertitude et GPS", () =>
        {
            var m = new CortexNativeCadenceMemory();
            Observe(m, 14);
            Require(Tier(m, pose: Pose(uncertainty: 1.4)) == 4);
            Require(Tier(m, pose: Pose(yaw: 11)) == 4);
            Require(Tier(m, health: LocalizationHealthState.Orange) == 4);
            Require(Tier(m, assessment: new(
                CortexTrackingQuality.Degraded, false, false, 0.1, "Conflit")) == 4);
            Require(Tier(m, risk: new(8, 4, 3, 4, 12, 15, "Dérive")) == 4);
        }, errors);

        Run("CORTEX F8 ne partage pas l'historique entre cartes", () =>
        {
            var m = new CortexNativeCadenceMemory();
            Observe(m, 14);
            Require(Tier(m, map: "woods") == 4);
            m.ObserveNative("woods", 1, Window, Good, Today);
            Require(Tier(m, map: "woods") == 4);
        }, errors);

        Run("CORTEX F8 historique survivant au redémarrage reste borné", () =>
        {
            var m = new CortexNativeCadenceMemory();
            Observe(m, 16);
            var snap = m.Export(Today);
            var other = new CortexNativeCadenceMemory();
            Require(other.Import(snap, Today) && other.MapCount == 1);
            Require(Tier(other) == 4); // current session never persisted
            other.ObserveNative(Map, 2, Window, null, Today);
            Observe(other, 2, raid: 2);
            Require(Tier(other, raid: 2) == 6);
            var bad = new CortexNativeCadenceMemory.Snapshot(1,
                [snap.Maps[0], snap.Maps[0]]);
            Require(!other.Import(bad, Today));
        }, errors);

        Run("CORTEX F8 historique expire et ne conserve aucune coordonnée", () =>
        {
            var m = new CortexNativeCadenceMemory();
            Observe(m, 10);
            var snap = m.Export(Today.AddDays(8));
            Require(snap.Maps.Count == 0);
            var serialized = System.Text.Json.JsonSerializer.Serialize(snap);
            Require(!serialized.Contains("Position", StringComparison.OrdinalIgnoreCase)
                && !serialized.Contains("Screenshot", StringComparison.OrdinalIgnoreCase)
                && !serialized.Contains("MemberId", StringComparison.OrdinalIgnoreCase));
        }, errors);

        Run("CORTEX F8 sauvegarde un profil sans images ni positions absolues", () =>
        {
            var path = Path.Combine(Path.GetTempPath(),
                "cortex-native-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var m = new CortexNativeCadenceMemory();
                Observe(m, 6);
                Require(CortexNativeCadenceStore.TrySave(m, path, Today));
                var loaded = new CortexNativeCadenceMemory();
                Require(CortexNativeCadenceStore.TryLoad(loaded, path, Today));
                Require(loaded.MapCount == 1 && loaded.CurrentClean == 0);
                var text = File.ReadAllText(path);
                Require(!text.Contains("Screenshot", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }, errors);
    }

    private static void Run(string label, Action test, ICollection<string> errors)
    {
        try { test(); Console.WriteLine("OK  " + label); }
        catch (Exception ex) { errors.Add(label + " : " + ex.Message); }
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Assertion F8 native cadence");
    }
}
