using System.Text.Json;

namespace RaidPilot.Launcher;

internal static class HabsGoalTests
{
    internal static void Run(Action<string, Action> run)
    {
        var now = new DateTimeOffset(2026, 11, 8, 1, 0, 0, TimeSpan.Zero);
        run("LNH : choix du match, Montréal à domicile ou à l'extérieur", () =>
        {
            var json = Board(Game(1, "OFF", now.AddHours(-25), "MTL", "TOR"),
                Game(2, "LIVE", now.AddHours(-1), "TOR", "MTL"), Game(3, "FUT", now.AddDays(1), "MTL", "BOS"));
            Check(HabsGoalFeed.SelectGame(json, now)?.Id == 2, "live home game missing");
            Check(HabsGoalFeed.SelectGame(Board(Game(4, "LIVE", now.AddHours(-1), "MTL", "TOR")), now)?.Id == 4, "away game missing");
            Check(HabsGoalFeed.SelectGame(Board(Game(5, "LIVE", now.AddHours(-10), "MTL", "TOR")), now) is null, "stale LIVE selected");
            Check(HabsGoalFeed.SelectGame(Board(Game(6, "LIVE", now, "TOR", "BOS")), now) is null, "wrong team selected");
        });
        run("LNH : lecture des buts réels, exclusion de l'adversaire et des tirs de barrage", () =>
        {
            var json = Pbp("MTL", "TOR", Play(11, 10, 8, 1, "05:00"), Play(12, 11, 10, 1, "05:30"), Play(13, 12, 8, 5, "00:00", "SO"));
            var snapshot = HabsGoalFeed.ParseGame(json, 12);
            Check(snapshot.Goals.Count == 1 && snapshot.Goals[0].EventId == 11, "non-MTL or shootout counted");
            Check(snapshot.ScoreLine == "MTL  2  —  0  TOR", "score formatting");
            Check(HabsGoalFeed.ParseGame(Pbp("TOR", "MTL", Play(14, 1, 8, 1, "05:00")), 12).Goals.Count == 1, "home goal missing");
            bool rejected = false;
            try { HabsGoalFeed.ParseGame(json, 99); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "wrong game accepted");
        });
        run("Lumières : aucun ancien but au démarrage, un déclenchement par nouveau but", () =>
        {
            var tracker = new HabsGoalTracker();
            Check(!tracker.Observe(Snap(12, 10, G(1, 5)), now), "startup celebration");
            Check(tracker.Observe(Snap(12, 20, G(1, 5), G(2, 19)), now.AddSeconds(20)), "new goal missed");
            Check(!tracker.Observe(Snap(12, 22, G(1, 5), G(2, 19)), now.AddSeconds(40)), "duplicate celebration");
        });
        run("Lumières : corrections, annulation et snapshots anciens sans doublon", () =>
        {
            var tracker = new HabsGoalTracker();
            tracker.Observe(Snap(12, 10, G(1, 5)), now);
            Check(!tracker.Observe(Snap(12, 15, new HabsGoal(99, 14, G(1, 5).Identity)), now.AddSeconds(20)), "reissued goal celebrated");
            Check(!tracker.Observe(Snap(12, 16), now.AddSeconds(40)), "removed goal celebrated");
            Check(!tracker.Observe(Snap(12, 18, G(1, 17)), now.AddSeconds(60)), "restored event celebrated");
            Check(!tracker.Observe(Snap(12, 9, G(3, 8)), now.AddSeconds(80)), "old insertion celebrated");
            Check(tracker.Observe(Snap(12, 25, G(4, 23)), now.AddSeconds(100)), "legitimate next goal missed");
        });
        run("Lumières : reprise après coupure et changement de match sans rattrapage", () =>
        {
            var tracker = new HabsGoalTracker();
            tracker.Observe(Snap(12, 10), now);
            Check(!tracker.Observe(Snap(12, 30, G(1, 25)), now.AddMinutes(3)), "old goal after outage");
            Check(tracker.Observe(Snap(12, 40, G(1, 25), G(2, 39)), now.AddMinutes(3).AddSeconds(20)), "next live goal missed");
            Check(!tracker.Observe(Snap(13, 50, G(1, 45)), now.AddMinutes(4)), "old goal in new game");
            tracker.Reset();
            Check(!tracker.Observe(Snap(13, 55, G(1, 45), G(2, 53)), now.AddMinutes(4).AddSeconds(20)), "enable replayed goal");
        });
        run("Lumières : but gagnant signalé à la fin du match", () =>
        {
            var tracker = new HabsGoalTracker();
            tracker.Observe(Snap(12, 10), now);
            Check(tracker.Observe(Snap(12, 20, G(1, 19)) with { State = "OFF" }, now.AddSeconds(20)), "winning goal missed");
            Check(!tracker.Observe(Snap(12, 20, G(1, 19)) with { State = "OFF" }, now.AddSeconds(40)), "final goal replayed");
        });
        run("Lumières : 2–0 vers 3–0, détail du but publié après les actions suivantes", () =>
        {
            var tracker = new HabsGoalTracker();
            var first = new HabsGoal(133, 88, "1:05:56");
            var second = new HabsGoal(270, 177, "1:13:06");
            var third = new HabsGoal(584, 438, "2:12:57");
            tracker.Observe(Snap(2026020268, 435, first, second), now);
            Check(!tracker.Observe(Snap(2026020268, 447, first, second) with { Score = 3 }, now.AddSeconds(10)), "unconfirmed score celebrated");
            Check(tracker.Observe(Snap(2026020268, 450, first, second, third), now.AddSeconds(20)), "late 3–0 goal missed");
            Check(!tracker.Observe(Snap(2026020268, 455, first, second, third), now.AddSeconds(30)), "late goal replayed");
        });
        run("Lumières : démarrage et reprise avec détail de but incomplet", () =>
        {
            var tracker = new HabsGoalTracker();
            tracker.Observe(Snap(12, 30, G(1, 10)) with { Score = 2 }, now);
            Check(!tracker.Observe(Snap(12, 40, G(1, 10), G(2, 25)), now.AddSeconds(10)), "delayed startup goal replayed");
            Check(tracker.Observe(Snap(12, 60, G(1, 10), G(2, 25), G(3, 50)), now.AddSeconds(20)), "next goal lost");
            tracker.Observe(Snap(12, 100, G(1, 10), G(2, 25), G(3, 50)) with { Score = 4 }, now.AddMinutes(3));
            Check(!tracker.Observe(Snap(12, 110, G(1, 10), G(2, 25), G(3, 50), G(4, 95)), now.AddMinutes(3).AddSeconds(10)), "delayed outage goal replayed");
        });
        run("Lumières : but gagnant retardé après le passage à FINAL", () =>
        {
            var tracker = new HabsGoalTracker();
            tracker.Observe(Snap(12, 100, G(1, 10)), now);
            Check(!tracker.Observe(Snap(12, 120, G(1, 10)) with { State = "FINAL", Score = 2 }, now.AddSeconds(10)), "unconfirmed final goal");
            Check(tracker.Observe(Snap(12, 125, G(1, 10), G(2, 115)) with { State = "OFF" }, now.AddSeconds(70)), "delayed winner missed");
        });
        run("Lumières : diagnostic local borné et panne d'écriture sans interruption", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "habs-log-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "test.log");
                File.WriteAllText(path, new string('a', HabsGoalDiagnostics.MaxBytes));
                HabsGoalDiagnostics.Write("new-goal\r\ndisplay", path);
                Check(new FileInfo(path + ".previous").Length == HabsGoalDiagnostics.MaxBytes, "rotation lost history");
                Check(File.ReadAllLines(path).Length == 1 && File.ReadAllText(path).Contains("new-goal  display"), "multiline event");
                HabsGoalDiagnostics.Write("must not throw", root); // A directory cannot be a log file.
            }
            finally { Directory.Delete(root, true); }
        });
        run("Lumières : dix secondes et fondus bornés", () =>
        {
            Check(HabsGoalPresentation.Visible(0) && HabsGoalPresentation.Visible(9.999), "premature end");
            Check(!HabsGoalPresentation.Visible(10) && !HabsGoalPresentation.Visible(12), "longer than ten seconds");
            Check(HabsGoalPresentation.Opacity(0) == 0 && HabsGoalPresentation.Opacity(1) == 255 && HabsGoalPresentation.Opacity(10) == 0, "fade boundaries");
            Check(!new HabsGoalSettings().Enabled, "opt-in default changed");
        });
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static HabsGoal G(int id, int order) => new(id, order, "1:00:" + id);
    private static HabsSnapshot Snap(long id, int order, params HabsGoal[] goals) => new(id, "LIVE", goals.Length, "MTL  2  —  0  TOR", order, goals);
    private static object Game(long id, string state, DateTimeOffset start, string away, string home) =>
        new { id, gameState = state, startTimeUTC = start, awayTeam = new { abbrev = away }, homeTeam = new { abbrev = home } };
    private static string Board(params object[] games) => JsonSerializer.Serialize(new { gamesByDate = new[] { new { games } } });
    private static object Play(int id, int order, int team, int period, string time, string kind = "REG") =>
        new { eventId = id, sortOrder = order, typeDescKey = "goal", timeInPeriod = time,
            periodDescriptor = new { number = period, periodType = kind }, details = new { eventOwnerTeamId = team } };
    private static string Pbp(string away, string home, params object[] plays) => JsonSerializer.Serialize(new {
        id = 12, gameState = "LIVE", awayTeam = new { abbrev = away, id = away == "MTL" ? 8 : 10, score = 2 },
        homeTeam = new { abbrev = home, id = home == "MTL" ? 8 : 10, score = 0 }, plays });
}
