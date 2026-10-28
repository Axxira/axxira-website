using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class MobileExactAnchorPolicyTests
{
    internal const int TestCount = 26;

    private static bool Ready(
        bool enabled = true,
        bool controls = true,
        bool moving = true,
        bool combat = false,
        bool screenshot = true,
        bool watcher = true,
        bool anchor = true,
        double age = 5,
        double cooldown = 5,
        double uncertainty = 4.0,
        LocalizationHealthState health = LocalizationHealthState.Red,
        bool trustedVisual = false,
        double native = 4) => MobileExactAnchorPolicy.ShouldCapture(
            enabled, controls, moving, combat, screenshot, watcher, anchor,
            age, cooldown, uncertainty, health, trustedVisual, true, native);

    internal static void RunAll(ICollection<string> failures)
    {
        Run("F8 mobile activé par défaut si aucune préférence", () =>
        {
            Require(MobileExactAnchorPreference.Load(Path.Combine(
                Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
            Require(!Ready(enabled: false));
        }, failures);
        Run("F8 mobile ne s'exécute pas hors du raid", () =>
            Require(!Ready(controls: false)), failures);
        Run("F8 mobile ne s'exécute pas sans mouvement", () =>
            Require(!Ready(moving: false)), failures);
        Run("F8 mobile ignore visée et combat", () =>
            Require(!Ready(combat: true)), failures);
        Run("F8 mobile exige un raccourci simple", () =>
            Require(!Ready(screenshot: false)), failures);
        Run("F8 mobile exige watcher présent", () =>
            Require(!Ready(watcher: false)), failures);
        Run("F8 mobile exige une première ancre exacte", () =>
            Require(!Ready(anchor: false)), failures);
        Run("F8 mobile attend quatre secondes après F8", () =>
            Require(!Ready(age: 3.99) && Ready(age: 4)), failures);
        Run("F8 mobile attend quatre secondes entre injections", () =>
            Require(!Ready(cooldown: 3.99) && Ready(cooldown: 4)), failures);
        Run("F8 mobile autorise première injection sans dernière tentative", () =>
            Require(Ready(cooldown: double.PositiveInfinity)), failures);
        Run("F8 mobile refuse temps invalides", () =>
            Require(!Ready(age: double.NaN)
                && !Ready(cooldown: double.NaN)
                && !Ready(age: -1)), failures);
        Run("F8 silencieux prolonge uniquement un consensus visuel local", () =>
        {
            Require(MobileExactAnchorPolicy.IsTrustedSilentCorrection(
                VisualRelocalizationSource.Consensus, false,
                0.95, 1.0, 0.3, 8.0));
        }, failures);
        Run("F8 silencieux refuse image isolée, atlas partagé et mesure aberrante", () =>
        {
            Require(!MobileExactAnchorPolicy.IsTrustedSilentCorrection(
                VisualRelocalizationSource.SuperKeyframe, false,
                0.98, 0.5, 0.2, 5.0));
            Require(!MobileExactAnchorPolicy.IsTrustedSilentCorrection(
                VisualRelocalizationSource.Consensus, true,
                0.98, 0.5, 0.2, 5.0));
            Require(!MobileExactAnchorPolicy.IsTrustedSilentCorrection(
                VisualRelocalizationSource.Consensus, false,
                0.98, 3.5, 0.2, 5.0));
            Require(!MobileExactAnchorPolicy.IsTrustedSilentCorrection(
                VisualRelocalizationSource.Consensus, false,
                double.NaN, 0.5, 0.2, 5.0));
        }, failures);
        Run("F8 adaptatif attend 12 s quand deux repères visuels sont corroborés", () =>
        {
            Require(!Ready(age: 10, cooldown: 10, uncertainty: 0.8,
                health: LocalizationHealthState.Yellow, trustedVisual: true));
            Require(Ready(age: 12, cooldown: 12, uncertainty: 0.8,
                health: LocalizationHealthState.Yellow, trustedVisual: true));
        }, failures);
        Run("F8 adaptatif attend 8 s en incertitude intermédiaire", () =>
        {
            Require(!Ready(age: 7, cooldown: 7, uncertainty: 1.8,
                health: LocalizationHealthState.Green, trustedVisual: true));
            Require(Ready(age: 8, cooldown: 8, uncertainty: 1.8,
                health: LocalizationHealthState.Green, trustedVisual: true));
        }, failures);
        Run("F8 adaptatif garde 4 s sans confirmation visuelle forte", () =>
        {
            Require(Ready(age: 4, cooldown: 4, uncertainty: 0.6,
                health: LocalizationHealthState.Green, trustedVisual: false));
        }, failures);
        Run("F8 adaptatif reprend 4 s si cap orange ou incertitude élevée", () =>
        {
            Require(Ready(age: 4, cooldown: 4, uncertainty: 0.8,
                health: LocalizationHealthState.Orange, trustedVisual: true));
            Require(Ready(age: 4, cooldown: 4, uncertainty: 3.0,
                health: LocalizationHealthState.Green, trustedVisual: true));
        }, failures);
        Run("F8 adaptatif refuse l'incertitude non finie", () =>
        {
            Require(!Ready(uncertainty: double.NaN, trustedVisual: true)
                && !Ready(uncertainty: double.PositiveInfinity, trustedVisual: true)
                && !Ready(uncertainty: -1, trustedVisual: true));
        }, failures);
        Run("F8 mobile conserve le choix après redémarrage", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(),
                "raidpilot-mobile-f8-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "mobile.json");
                Require(MobileExactAnchorPreference.TrySave(true, path));
                Require(MobileExactAnchorPreference.Load(path));
                Require(MobileExactAnchorPreference.TrySave(false, path));
                Require(!MobileExactAnchorPreference.Load(path));
            }
            finally { Directory.Delete(directory, recursive: true); }
        }, failures);
        Run("F8 mobile ignore configuration incompatible", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(),
                "raidpilot-mobile-f8-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "mobile.json");
                File.WriteAllText(path, "{\"schemaVersion\":2,\"enabled\":true}");
                Require(!MobileExactAnchorPreference.Load(path));
                File.WriteAllText(path, "{broken");
                Require(!MobileExactAnchorPreference.Load(path));
                File.WriteAllText(path, "{\"schemaVersion\":\"1\",\"enabled\":true}");
                Require(!MobileExactAnchorPreference.Load(path));
            }
            finally { Directory.Delete(directory, recursive: true); }
        }, failures);
        Run("F8 mobile marque dès sa demande la calibration contaminée", () =>
        {
            var t = Stopwatch.Frequency * 10;
            var request = new AutomaticReanchorRequest(
                t, DateTimeOffset.UtcNow, 22,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                requestedWhileMoving: true);
            Require(request.RequestedWhileMoving && request.MovementOccurred);
            var idle = new AutomaticReanchorRequest(
                t, DateTimeOffset.UtcNow, 22,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            Require(!idle.RequestedWhileMoving && !idle.MovementOccurred);
        }, failures);

        Run("F8 historique autorise un palier de six secondes, pas avant", () =>
        {
            Require(!Ready(age: 5.9, cooldown: 5.9, uncertainty: 0.8,
                health: LocalizationHealthState.Green, native: 6));
            Require(Ready(age: 6, cooldown: 6, uncertainty: 0.8,
                health: LocalizationHealthState.Green, native: 6));
        }, failures);

        Run("F8 historique autorise huit secondes mais pas douze", () =>
        {
            Require(!Ready(age: 7, cooldown: 7, uncertainty: 0.8,
                health: LocalizationHealthState.Green, native: 8));
            Require(Ready(age: 8, cooldown: 8, uncertainty: 0.8,
                health: LocalizationHealthState.Green, native: 8));
            Require(!Ready(age: 8, cooldown: 8, uncertainty: 0.8,
                health: LocalizationHealthState.Green,
                trustedVisual: true, native: 8));
        }, failures);

        Run("F8 historique ne contourne jamais santé ou forte incertitude", () =>
        {
            Require(Ready(age: 4, cooldown: 4, uncertainty: 1.6,
                health: LocalizationHealthState.Green, native: 8));
            Require(Ready(age: 4, cooldown: 4, uncertainty: 0.8,
                health: LocalizationHealthState.Orange, native: 8));
            Require(Ready(age: 4, cooldown: 4, uncertainty: 0.8,
                health: LocalizationHealthState.Red, native: 8));
        }, failures);

        Run("F8 historique ne contourne jamais action ou capture indisponible", () =>
        {
            Require(!Ready(age: 8, cooldown: 8, uncertainty: 0.8,
                health: LocalizationHealthState.Green, native: 8, combat: true));
            Require(!Ready(age: 8, cooldown: 8, uncertainty: 0.8,
                health: LocalizationHealthState.Green, native: 8, screenshot: false));
            Require(!Ready(age: 8, cooldown: 8, uncertainty: 0.8,
                health: LocalizationHealthState.Green, native: 8, controls: false));
            Require(Ready(age: 4, cooldown: 4, uncertainty: 0.8,
                health: LocalizationHealthState.Green, native: double.PositiveInfinity));
        }, failures);

        Run("F8 mobile borne la dérive en simulation, sans prétendre mesurer EFT", () =>
        {
            var continuous = new RealtimePoseEstimator();
            var anchored = new RealtimePoseEstimator();
            var first = new ScreenshotObservation("synthetic-F8", 0, 0, 0,
                0, 0, 0, 1, 0);
            continuous.Anchor(first);
            anchored.Anchor(first);
            var worstAuto = 0.0;
            for (var seconds = 1; seconds <= 20; seconds++)
            {
                var realZ = seconds * 3.0; // Synthetic EFT reference path.
                continuous.Advance(1, 1, 0, false, false, 0, false);
                anchored.Advance(1, 1, 0, false, false, 0, false);
                worstAuto = Math.Max(worstAuto,
                    Math.Abs(anchored.CurrentPose!.Value.Z - realZ));
                if (seconds % 4 == 0)
                    anchored.Anchor(new ScreenshotObservation(
                        "synthetic-F8", 0, 0, realZ, 0, 0, 0, 1, 0));
            }
            var baselineDrift = Math.Abs(continuous.CurrentPose!.Value.Z - 60);
            Require(baselineDrift > 10 && worstAuto < baselineDrift / 2);
        }, failures);
    }

    private static void Run(string name, Action action, ICollection<string> failures)
    {
        try { action(); Console.WriteLine("OK  " + name); }
        catch (Exception error) { failures.Add(name + " : " + error.Message); }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Test F8 mobile échoué.");
    }
}
