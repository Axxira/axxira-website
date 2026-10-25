using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class MobileExactAnchorPolicyTests
{
    internal const int TestCount = 15;

    private static bool Ready(
        bool enabled = true,
        bool controls = true,
        bool moving = true,
        bool combat = false,
        bool screenshot = true,
        bool watcher = true,
        bool anchor = true,
        double age = 5,
        double cooldown = 5) => MobileExactAnchorPolicy.ShouldCapture(
            enabled, controls, moving, combat, screenshot, watcher, anchor, age, cooldown);

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
