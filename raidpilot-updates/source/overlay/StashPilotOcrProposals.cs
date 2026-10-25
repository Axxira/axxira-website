using System.Text.RegularExpressions;

namespace RaidPilot.Overlay;

/// <summary>
/// Strict parser for OCR inventory text. Produces review-only suggestions:
/// neither the item identity nor FIR status is asserted by OCR.
/// </summary>
internal static partial class StashPilotOcrProposals
{
    private static readonly Regex PrefixQuantity = new(
        @"^\s*(?<count>\d{1,4})\s*[x×]\s+(?<item>[^;\r\n]{2,90})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SuffixQuantity = new(
        @"^\s*(?<item>[^;\r\n]{2,90}?)\s+[x×]\s*(?<count>\d{1,4})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static IReadOnlyList<StashPilotInventoryItem> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new List<StashPilotInventoryItem>();
        foreach (var line in lines.Take(160))
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length > 110)
                continue;
            var match = PrefixQuantity.Match(line);
            if (!match.Success) match = SuffixQuantity.Match(line);
            if (!match.Success
                || !int.TryParse(match.Groups["count"].Value, out var count)
                || count is <= 0 or > 1000)
                continue;
            var item = match.Groups["item"].Value.Trim();
            if (item.Length is < 2 or > 90
                || item.Any(char.IsControl)
                || item.Contains(';'))
                continue;
            result.Add(new StashPilotInventoryItem(item, count, false, false));
        }
        return result;
    }

    internal static string AsReviewLines(IEnumerable<StashPilotInventoryItem> proposed)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        return string.Join(Environment.NewLine, proposed.Take(160)
            .Select(i => i.Item + ";" + i.Quantity + ";;"));
    }
}
