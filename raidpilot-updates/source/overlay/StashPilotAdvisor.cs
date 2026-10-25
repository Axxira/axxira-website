namespace RaidPilot.Overlay;

/// <summary>
/// Conservative inventory advisor. An unverified item name is NEVER safe to
/// sell. Needed quantities are summed across selected quests and hideout
/// upgrades; Found in Raid requirements are tracked separately.
/// </summary>
internal static class StashPilotAdvisor
{
    internal static IReadOnlyList<StashPilotDecision> Analyze(
        IEnumerable<StashPilotInventoryItem> inventory,
        IEnumerable<StashPilotRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(requirements);
        var needs = requirements
            .Where(r => !string.IsNullOrWhiteSpace(r.Item)
                && r.Quantity > 0 && r.Confirmed)
            .GroupBy(r => Normalize(r.Item), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new
            {
                Total = g.Sum(r => (long)r.Quantity),
                FoundInRaid = g.Where(r => r.FoundInRaidRequired)
                    .Sum(r => (long)r.Quantity),
                Reasons = string.Join(", ", g.Select(r => r.Source)
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(5))
            }, StringComparer.OrdinalIgnoreCase);
        var ownedByItem = inventory
            .Where(i => i.Quantity > 0 && !string.IsNullOrWhiteSpace(i.Item))
            .GroupBy(i => Normalize(i.Item), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Quantity),
                StringComparer.OrdinalIgnoreCase);
        var firByItem = inventory
            .Where(i => i.Quantity > 0 && i.FoundInRaid && i.Confirmed)
            .GroupBy(i => Normalize(i.Item), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Quantity),
                StringComparer.OrdinalIgnoreCase);

        var results = new List<StashPilotDecision>();
        foreach (var row in inventory)
        {
            if (row.Quantity <= 0 || string.IsNullOrWhiteSpace(row.Item))
                continue;
            var key = Normalize(row.Item);
            needs.TryGetValue(key, out var need);
            var totalRequired = need?.Total ?? 0;
            var firRequired = need?.FoundInRaid ?? 0;
            ownedByItem.TryGetValue(key, out var totalOwned);
            firByItem.TryGetValue(key, out var firOwned);

            // No verified complete progression = absence of a listed
            // requirement is NOT evidence that this item is useless.
            var necessary = Math.Min(totalOwned, totalRequired);
            var excess = Math.Max(0, totalOwned - totalRequired);
            var firShort = Math.Max(0, firRequired - firOwned);
            var label = !row.Confirmed
                ? "À vérifier"
                : totalRequired <= 0
                    ? "Utilité non confirmée"
                    : firShort > 0 && row.FoundInRaid
                        ? "Priorité quête FIR"
                        : "À conserver";
            results.Add(new(row.Item, row.Quantity, label,
                totalRequired, necessary, excess, firShort,
                need?.Reasons ?? "Aucun besoin confirmé dans les objectifs saisis",
                row.Confirmed && totalRequired > 0));
        }
        return results;
    }

    internal static IReadOnlyList<StashPilotInventoryItem> ParseInventory(string text)
    {
        var output = new List<StashPilotInventoryItem>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(';', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts.Length > 4
                || parts[0].Length == 0
                || !int.TryParse(parts[1], out var count)
                || count is <= 0 or > 100000)
                continue;
            var fir = parts.Length > 2 &&
                parts[2].Equals("FIR", StringComparison.OrdinalIgnoreCase);
            var confirmed = parts.Length > 3 &&
                parts[3].Equals("CONFIRMÉ", StringComparison.OrdinalIgnoreCase);
            output.Add(new(parts[0], count, fir, confirmed));
        }
        return output;
    }

    internal static IReadOnlyList<StashPilotRequirement> ParseNeeds(string text)
    {
        var output = new List<StashPilotRequirement>();
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(';', StringSplitOptions.TrimEntries);
            if (parts.Length < 3 || parts.Length > 5
                || parts[0].Length == 0 || parts[2].Length == 0
                || !int.TryParse(parts[1], out var count)
                || count is <= 0 or > 100000)
                continue;
            output.Add(new(parts[0], count, parts[2],
                parts.Length > 3 && parts[3].Equals("FIR",
                    StringComparison.OrdinalIgnoreCase),
                parts.Length > 4 && parts[4].Equals("CONFIRMÉ",
                    StringComparison.OrdinalIgnoreCase)));
        }
        return output;
    }

    private static string Normalize(string item) => item.Trim().ToUpperInvariant();
}
internal readonly record struct StashPilotInventoryItem(
    string Item, int Quantity, bool FoundInRaid, bool Confirmed);
internal readonly record struct StashPilotRequirement(
    string Item, int Quantity, string Source,
    bool FoundInRaidRequired, bool Confirmed);
internal readonly record struct StashPilotDecision(
    string Item, int Quantity, string Recommendation,
    long Required, long ToKeep, long Excess,
    long MissingFoundInRaid, string Reasons, bool VerifiedNeed);
