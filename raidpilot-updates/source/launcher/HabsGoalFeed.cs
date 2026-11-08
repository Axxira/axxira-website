using System.Text.Json;

namespace RaidPilot.Launcher;

internal sealed record HabsGame(long Id, DateTimeOffset Start, string State);
internal sealed record HabsGoal(int EventId, int Order, string Identity);
internal sealed record HabsSnapshot(long GameId, string State, int Score, string ScoreLine,
    int LastOrder, IReadOnlyList<HabsGoal> Goals);

// Read-only public NHL data. Neither this feed nor the companion touches game memory/input.
internal static class HabsGoalFeed
{
    internal const string ScoreboardUrl = "https://api-web.nhle.com/v1/scoreboard/MTL/now";
    internal static bool IsLive(string state) => state is "LIVE" or "CRIT";

    internal static HabsGame? SelectGame(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var games = new List<HabsGame>();
        foreach (var day in doc.RootElement.GetProperty("gamesByDate").EnumerateArray())
        foreach (var game in day.GetProperty("games").EnumerateArray())
        {
            if (Text(game.GetProperty("awayTeam"), "abbrev") != "MTL" &&
                Text(game.GetProperty("homeTeam"), "abbrev") != "MTL") continue;
            var start = game.GetProperty("startTimeUTC").GetDateTimeOffset();
            var state = Text(game, "gameState");
            // Ignore stale LIVE records and future games. Keep a recent final for the winning goal.
            if (start > now.AddMinutes(30) || start < now.AddHours(-8) ||
                !(IsLive(state) || state is "PRE" or "FINAL" or "OFF")) continue;
            games.Add(new(game.GetProperty("id").GetInt64(), start, state));
        }
        return games.OrderByDescending(g => IsLive(g.State)).ThenByDescending(g => g.Start).FirstOrDefault();
    }

    internal static HabsSnapshot ParseGame(string json, long expectedId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetInt64();
        var away = root.GetProperty("awayTeam");
        var home = root.GetProperty("homeTeam");
        var awayName = Text(away, "abbrev");
        var homeName = Text(home, "abbrev");
        if (id != expectedId || (awayName != "MTL" && homeName != "MTL"))
            throw new InvalidDataException("Unexpected NHL game.");
        var team = awayName == "MTL" ? away : home;
        var teamId = team.GetProperty("id").GetInt32();
        var goals = new List<HabsGoal>();
        int lastOrder = 0;
        foreach (var play in root.GetProperty("plays").EnumerateArray())
        {
            var order = Number(play, "sortOrder");
            lastOrder = Math.Max(lastOrder, order);
            if (Text(play, "typeDescKey") != "goal" || !play.TryGetProperty("details", out var details) ||
                Number(details, "eventOwnerTeamId") != teamId) continue;
            var period = play.GetProperty("periodDescriptor");
            if (Text(period, "periodType") == "SO") continue; // Shootout attempts aren't scoreboard goals.
            var identity = $"{Number(period, "number")}:{Text(play, "timeInPeriod")}";
            goals.Add(new(Number(play, "eventId"), order, identity));
        }
        return new(id, Text(root, "gameState"), team.GetProperty("score").GetInt32(),
            $"{awayName}  {away.GetProperty("score").GetInt32()}  —  {home.GetProperty("score").GetInt32()}  {homeName}",
            lastOrder, goals);
    }

    private static string Text(JsonElement node, string key) =>
        node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static int Number(JsonElement node, string key) =>
        node.TryGetProperty(key, out var value) && value.TryGetInt32(out var n) ? n : 0;
}

internal sealed class HabsGoalTracker
{
    private long _game;
    private int _order;
    private DateTimeOffset _lastSuccess;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<int> _seenIds = [];

    internal void Reset() { _game = 0; _order = 0; _seen.Clear(); _seenIds.Clear(); }

    internal bool Observe(HabsSnapshot snapshot, DateTimeOffset now)
    {
        var baseline = snapshot.GameId != _game || now - _lastSuccess > TimeSpan.FromSeconds(90);
        if (snapshot.GameId != _game) Reset();
        var fresh = snapshot.Goals.Any(g => g.Order > _order && !_seen.Contains(g.Identity) && !_seenIds.Contains(g.EventId));
        foreach (var goal in snapshot.Goals) { _seen.Add(goal.Identity); _seenIds.Add(goal.EventId); }
        _game = snapshot.GameId;
        _order = Math.Max(_order, snapshot.LastOrder);
        _lastSuccess = now;
        return !baseline && fresh && (HabsGoalFeed.IsLive(snapshot.State) || snapshot.State is "FINAL" or "OFF");
    }
}

internal sealed record HabsGoalSettings(bool Enabled = false);
internal static class HabsGoalPresentation
{
    internal const double DurationSeconds = 10;
    internal static bool Visible(double elapsed) => elapsed >= 0 && elapsed < DurationSeconds;
    internal static byte Opacity(double elapsed) => (byte)(255 * Math.Clamp(Math.Min(elapsed / .15, (DurationSeconds - elapsed) / .45), 0, 1));
}

internal static class HabsGoalSettingsStore
{
    internal static string SettingsPath => Path.Combine(LauncherSettingsStore.SettingsDirectory, "habs-goals.json");
    internal static HabsGoalSettings Load()
    {
        try { return JsonSerializer.Deserialize<HabsGoalSettings>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch { return new(); }
    }
    internal static void Save(HabsGoalSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, SettingsPath, true);
    }
}
