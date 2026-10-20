using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RaidPilot.Overlay;

public sealed record QuestContextPoint(double X, double? Y, double Z);
public sealed record QuestContextLocation(
    string Id, string Map, QuestContextPoint Position,
    IReadOnlyList<QuestContextPoint> Outline, bool Possible, string Place);
public sealed record QuestContextObjective(
    string Id, string Type, string Instruction, string Language,
    bool Optional, string Requirements, IReadOnlyList<QuestContextLocation> Locations);

/// <summary>Public task data only. No inferred quest progress, room, key name or completion.</summary>
internal static class QuestContextData
{
    internal static IReadOnlyList<QuestContextObjective> Parse(
        JsonElement objectives, IReadOnlySet<string> mapIds, string normalizedMap,
        IReadOnlyDictionary<string, string> french, IReadOnlyDictionary<string, string> english)
    {
        var result = new List<QuestContextObjective>();
        if (objectives.ValueKind != JsonValueKind.Array) return result;
        foreach (var objective in objectives.EnumerateArray().Take(100))
        {
            if (objective.ValueKind != JsonValueKind.Object) continue;
            var id = String(objective, "id");
            var type = String(objective, "type");
            var (instruction, language) = Translate(String(objective, "description"), french, english);
            if (id.Length == 0 || instruction.Length == 0) continue;
            var locations = new List<QuestContextLocation>();
            foreach (var zone in Array(objective, "zones").Take(256))
            {
                if (!mapIds.Contains(String(zone, "map"))) continue;
                if (!Property(zone, "position", out var rawPosition) || Point(rawPosition) is not { } point) continue;
                var outline = Array(zone, "outline").Take(256).Select(Point)
                    .Where(p => p is not null).Select(p => p!).ToArray();
                // Contradictory public position/outline coordinates cannot define a reliable nearby zone.
                // Do not guess which representation is correct (observed in one Woods objective).
                if (outline.Length >= 3 && (point.X < outline.Min(p => p.X) - 25
                    || point.X > outline.Max(p => p.X) + 25 || point.Z < outline.Min(p => p.Z) - 25
                    || point.Z > outline.Max(p => p.Z) + 25)) continue;
                var (place, _) = Translate(String(zone, "name"), french, english);
                // Internal zone IDs are not human-readable location descriptions.
                if (place.Contains('_')) place = "";
                locations.Add(new QuestContextLocation(
                    id + ":zone:" + locations.Count, normalizedMap, point, outline, false, place));
            }
            foreach (var location in Array(objective, "possibleLocations").Take(256))
            {
                if (!mapIds.Contains(String(location, "map"))) continue;
                foreach (var rawPoint in Array(location, "positions").Take(256))
                {
                    if (Point(rawPoint) is not { } point) continue;
                    var (place, _) = Translate(String(location, "name"), french, english);
                    locations.Add(new QuestContextLocation(
                        id + ":possible:" + locations.Count, normalizedMap, point,
                        System.Array.Empty<QuestContextPoint>(), true, place));
                }
            }
            if (locations.Count == 0) continue;
            var unique = locations.DistinctBy(l => (
                Math.Round(l.Position.X, 2), l.Position.Y.HasValue ? Math.Round(l.Position.Y.Value, 2) : double.NaN,
                Math.Round(l.Position.Z, 2), l.Possible)).ToArray();
            result.Add(new QuestContextObjective(id, type, instruction, language,
                Property(objective, "optional", out var optional) && optional.ValueKind == JsonValueKind.True,
                Requirements(objective, type), unique));
        }
        return result;
    }

    private static string Requirements(JsonElement objective, string type)
    {
        var tags = new List<string>();
        if (Array(objective, "requiredKeys").Any(group => group.ValueKind == JsonValueKind.Array && group.GetArrayLength() > 0))
            tags.Add("Clé requise");
        if (String(objective, "markerItem").Length > 0) tags.Add("Marqueur requis");
        if (type is "plantItem" or "plantQuestItem") tags.Add("Objet à déposer");
        if (new[] { "usingWeapon", "usingWeaponMods", "wearing", "notWearing" }.Any(k => Array(objective, k).Any()))
            tags.Add("Conditions d'équipement");
        if (Number(objective, "timeFromHour") is { } start && Number(objective, "timeUntilHour") is { } end
            && start >= 0 && start <= 24 && end >= 0 && end <= 24 && start != end)
            tags.Add(FormattableString.Invariant($"Horaire : {start:0.##}–{end:0.##} h"));
        if (Array(objective, "playerHealthEffect").Any() || Array(objective, "enemyHealthEffect").Any())
            tags.Add("Conditions d'état de santé");
        return string.Join(" · ", tags);
    }

    internal static (string Text, string Language) Translate(string key,
        IReadOnlyDictionary<string, string> french, IReadOnlyDictionary<string, string> english)
    {
        if (french.TryGetValue(key, out var fr) && !string.IsNullOrWhiteSpace(fr)) return (Clean(fr), "fr");
        if (english.TryGetValue(key, out var en) && !string.IsNullOrWhiteSpace(en)) return (Clean(en), "en");
        // A translation key must not leak into an instruction.
        if (string.IsNullOrWhiteSpace(key) || Regex.IsMatch(key, @"^[0-9a-f]{24}(?:\s.*)?$", RegexOptions.IgnoreCase)
            || key.Contains('_')) return ("", "");
        return (Clean(key), "source");
    }

    private static string Clean(string value)
    {
        value = WebUtility.HtmlDecode(value);
        value = Regex.Replace(value, "<[^>]*>", " ");
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value.Length <= 1600 ? value : value[..1597] + "…";
    }

    private static QuestContextPoint? Point(JsonElement value)
    {
        if (Number(value, "x") is not { } x || Number(value, "z") is not { } z) return null;
        double? y = null;
        if (Property(value, "y", out var rawY))
        {
            if (rawY.ValueKind != JsonValueKind.Number || !rawY.TryGetDouble(out var number)
                || !double.IsFinite(number) || Math.Abs(number) > 20000) return null;
            y = number;
        }
        if (Math.Abs(x) > 20000 || Math.Abs(z) > 20000) return null;
        return new(x, y, z);
    }
    private static double? Number(JsonElement obj, string key) =>
        Property(obj, key, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
    private static string String(JsonElement obj, string key) =>
        Property(obj, key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
    private static bool Property(JsonElement obj, string key, out JsonElement value)
    {
        value = default;
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out value);
    }
    private static IEnumerable<JsonElement> Array(JsonElement obj, string key) =>
        Property(obj, key, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray() : System.Array.Empty<JsonElement>();
}

public sealed record QuestContextHint(string Key, string QuestId, string Name, string Where,
    string Instruction, string Note, double Distance, int NearbyCount, string Source);

/// <summary>Conservative proximity selector; its distance is never proof of objective completion.</summary>
internal sealed class QuestContextAdvisor
{
    internal const double EnterDistance = 55.0;
    internal const double LeaveDistance = 75.0;
    internal const double MaximumUncertainty = 8.0;
    private string? _previous;
    private string? _map;
    private int _generation = -1;

    internal void Reset() { _previous = _map = null; _generation = -1; }

    internal QuestContextHint? Select(IEnumerable<QuestCandidate> quests, IReadOnlySet<string>? recognized,
        string map, int generation, RealtimePose? pose, double ageSeconds, bool active)
    {
        if (_map != map || _generation != generation) { _previous = null; _map = map; _generation = generation; }
        if (!active || pose is not { } p || recognized is null || recognized.Count == 0
            || !double.IsFinite(ageSeconds) || ageSeconds < 0 || ageSeconds > 2.0
            || !new[] { p.X, p.Y, p.Z, p.YawDegrees, p.PositionUncertaintyMeters }.All(double.IsFinite)
            || p.PositionUncertaintyMeters < 0 || p.PositionUncertaintyMeters > MaximumUncertainty)
        { _previous = null; return null; }
        var choices = new List<(QuestCandidate Quest, QuestContextObjective Objective, QuestContextLocation Location, string Key, double Distance)>();
        foreach (var quest in quests.Where(q => recognized.Contains(q.Id)))
        foreach (var objective in quest.Objectives)
        {
            var nearest = objective.Locations.Where(l => l.Map == map)
                .Select(l => (Location: l, Distance: DistanceTo(p, l)))
                .Where(l => double.IsFinite(l.Distance))
                .OrderBy(l => l.Distance).FirstOrDefault();
            if (nearest.Location is null) continue;
            var key = quest.Id + ":" + objective.Id;
            var radius = key == _previous ? LeaveDistance : EnterDistance;
            if (nearest.Distance + p.PositionUncertaintyMeters <= radius)
                choices.Add((quest, objective, nearest.Location, key, nearest.Distance));
        }
        if (choices.Count == 0) { _previous = null; return null; }
        var best = choices.OrderBy(c => c.Distance + (c.Objective.Optional ? 5 : 0))
            .ThenBy(c => c.Key, StringComparer.Ordinal).First();
        var previous = choices.FirstOrDefault(c => c.Key == _previous);
        if (previous.Quest is not null && previous.Distance <= best.Distance + 8) best = previous;
        _previous = best.Key;
        var location = best.Location;
        var distance = Math.Round(best.Distance / 5.0) * 5;
        var place = location.Place.Length > 0 ? location.Place
            : location.Possible ? "Emplacement possible · repère violet" : "Zone d'objectif · repère violet";
        var altitude = location.Position.Y is { } y && Math.Abs(y - p.Y) >= 4
            ? $" · niveau à vérifier ({(y > p.Y ? "↑" : "↓")} ≈{Math.Round(Math.Abs(y - p.Y))} m)"
            : " · étage non confirmé";
        var where = $"{place} · ≈{distance:0} m{altitude}";
        var tags = new List<string>();
        if (best.Objective.Optional) tags.Add("Facultatif");
        if (best.Objective.Requirements.Length > 0) tags.Add(best.Objective.Requirements);
        if (best.Objective.Language == "en") tags.Add("Texte anglais : traduction FR indisponible");
        tags.Add("Progression non vérifiée");
        if (choices.Count > 1) tags.Add($"+{choices.Count - 1} objectif(s) proche(s)");
        return new(best.Key, best.Quest.Id, best.Quest.Name, where, best.Objective.Instruction,
            string.Join(" · ", tags), distance, choices.Count, "tarkov.dev/tasks");
    }

    private static double DistanceTo(RealtimePose p, QuestContextLocation location)
    {
        var outline = location.Outline;
        if (outline.Count < 3) return Math.Sqrt(Math.Pow(p.X - location.Position.X, 2) + Math.Pow(p.Z - location.Position.Z, 2));
        var inside = false;
        var distanceSquared = double.PositiveInfinity;
        for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
        {
            var a = outline[j]; var b = outline[i];
            if ((a.Z > p.Z) != (b.Z > p.Z) && p.X < (b.X - a.X) * (p.Z - a.Z) / (b.Z - a.Z) + a.X)
                inside = !inside;
            var dx = b.X - a.X; var dz = b.Z - a.Z;
            var denom = dx * dx + dz * dz;
            var t = denom > 0 ? Math.Clamp(((p.X - a.X) * dx + (p.Z - a.Z) * dz) / denom, 0, 1) : 0;
            distanceSquared = Math.Min(distanceSquared, Math.Pow(p.X - a.X - t * dx, 2) + Math.Pow(p.Z - a.Z - t * dz, 2));
        }
        return inside ? 0 : Math.Sqrt(distanceSquared);
    }
}
