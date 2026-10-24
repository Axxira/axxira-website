using System.Text.Json;
using System.Text.RegularExpressions;

namespace RaidPilot.Overlay;

/// <summary>Only public EFT task IDs, explicit status and log timestamp. No raw messages, profile IDs or chat.</summary>
internal sealed record EftQuestLogEvent(string TaskId, string Status, long AtUnixSeconds)
{
    internal const string Source = "eft-local-log";
}

internal static partial class EftQuestLogParser
{
    [GeneratedRegex(@"^[0-9a-fA-F]{24}$", RegexOptions.CultureInvariant)]
    private static partial Regex TaskIdPattern();

    /// <summary>Reject all other notifications; a quest type is NOT an objective-level confirmation.</summary>
    internal static bool TryParseNotification(string? json, out EftQuestLogEvent? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65_536) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 10 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var kind)
                || kind.ValueKind != JsonValueKind.String
                || kind.GetString() != "new_message"
                || !root.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.Number
                || !type.TryGetInt32(out var numeric)
                || numeric is not (10 or 11 or 12)
                || !message.TryGetProperty("templateId", out var template)
                || template.ValueKind != JsonValueKind.String
                || !message.TryGetProperty("dt", out var timestamp)
                || timestamp.ValueKind != JsonValueKind.Number
                || !timestamp.TryGetInt64(out var at)
                || at < 1_500_000_000 || at > 4_102_444_800)
                return false;
            var full = template.GetString() ?? "";
            var id = full.Split([' ', '\t', '\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
            if (!TaskIdPattern().IsMatch(id)) return false;
            result = new EftQuestLogEvent(id.ToLowerInvariant(),
                numeric == 10 ? "active" : numeric == 11 ? "failed" : "completed", at);
            return true;
        }
        catch (JsonException) { return false; }
    }

}
