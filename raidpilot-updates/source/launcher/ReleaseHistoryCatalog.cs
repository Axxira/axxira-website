using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaidPilot.Launcher;

/// <summary>
/// Release history is authored with each build and then embedded in the stable
/// update manifest. No version/date is hardcoded in the launcher UI itself.
/// </summary>
internal sealed record ReleaseHistoryEntry
{
    [JsonPropertyName("component")]
    public string Component { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("raidPilotVersion")]
    public string RaidPilotVersion { get; init; } = string.Empty;

    [JsonPropertyName("beaconVersion")]
    public string BeaconVersion { get; init; } = string.Empty;

    [JsonPropertyName("launcherVersion")]
    public string LauncherVersion { get; init; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("details")]
    public string Details { get; init; } = string.Empty;
}

internal sealed record ReleaseHistoryFile
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("entries")]
    public List<ReleaseHistoryEntry>? Entries { get; init; }
}

internal static class ReleaseHistoryCatalog
{
    private const int MaxEntries = 24;
    private const int MaxFileBytes = 64 * 1024;

    internal static IReadOnlyList<ReleaseHistoryEntry> FromManifest(
        UpdateManifest? manifest, IReadOnlyList<ReleaseHistoryEntry>? fallback = null)
    {
        var downloaded = Normalize(manifest?.ReleaseHistory);
        if (downloaded.Count > 0)
            return downloaded;
        return Normalize(fallback);
    }

    internal static IReadOnlyList<ReleaseHistoryEntry> ReadBundled(string? filePath = null)
    {
        try
        {
            var path = filePath ?? Path.Combine(
                AppContext.BaseDirectory, "release-history.json");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is < 10 or > MaxFileBytes)
                return [];

            var bytes = File.ReadAllBytes(info.FullName);
            var parsed = JsonSerializer.Deserialize<ReleaseHistoryFile>(
                bytes, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    MaxDepth = 8,
                });
            return parsed?.SchemaVersion == 1
                ? Normalize(parsed.Entries)
                : [];
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or JsonException or System.Security.SecurityException)
        {
            // A missing offline history never prevents the launcher from opening.
            return [];
        }
    }

    internal static IReadOnlyList<ReleaseHistoryEntry> Normalize(
        IEnumerable<ReleaseHistoryEntry>? source)
    {
        if (source is null)
            return [];

        var accepted = new List<ReleaseHistoryEntry>(MaxEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source.Take(64))
        {
            if (entry is null
                || (entry.Component != "raidpilot"
                    && entry.Component != "beacon"
                    && entry.Component != "launcher")
                || !Version.TryParse(entry.Version, out _)
                || !Version.TryParse(entry.RaidPilotVersion, out _)
                || !Version.TryParse(entry.BeaconVersion, out _)
                || !Version.TryParse(entry.LauncherVersion, out _)
                || !DateOnly.TryParseExact(
                    entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)
                || string.IsNullOrWhiteSpace(entry.Summary)
                || entry.Summary.Length > 115
                || string.IsNullOrWhiteSpace(entry.Details)
                || entry.Details.Length > 2000
                || entry.Summary.Any(char.IsControl)
                || entry.Details.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')
                || !seen.Add(entry.Component + ":" + entry.Version))
                continue;

            accepted.Add(entry);
            if (accepted.Count >= MaxEntries)
                break;
        }
        return accepted;
    }

    internal static string FormatDetailed(
        UpdateManifest? manifest,
        IReadOnlyList<ReleaseHistoryEntry>? fallback = null)
    {
        var entries = FromManifest(manifest, fallback);
        if (entries.Count == 0)
            return FormatLatestComponents(manifest);

        var text = new StringBuilder(4096);
        text.AppendLine("HISTORIQUE DES MISES À JOUR");
        text.AppendLine("Toutes les versions sont publiées sur le canal Stable.");
        text.AppendLine();
        foreach (var entry in entries)
        {
            text.Append("v").Append(entry.Version)
                .Append("  ·  ").Append(ComponentLabel(entry.Component))
                .Append("  ·  ").AppendLine(entry.Date);
            text.AppendLine(entry.Summary);
            text.AppendLine(entry.Details.Trim());
            text.AppendLine();
            text.AppendLine(new string('─', 48));
            text.AppendLine();
        }
        return text.ToString().Trim();
    }

    private static string FormatLatestComponents(UpdateManifest? manifest)
    {
        if (manifest is null)
            return "Historique temporairement indisponible. Vérifie la connexion au canal Stable.";

        var parts = new List<string>();
        if (manifest.RaidPilot is { } overlay)
            parts.Add($"RaidPilot v{overlay.Version}\n{overlay.ReleaseNotes ?? "Aucune note fournie."}");
        if (manifest.Beacon is { } beacon)
            parts.Add($"Beacon v{beacon.Version}\n{beacon.ReleaseNotes ?? "Aucune note fournie."}");
        if (manifest.Launcher is { } launcher)
            parts.Add($"Launcher v{launcher.Version}\n{launcher.ReleaseNotes ?? "Aucune note fournie."}");
        return parts.Count > 0
            ? string.Join("\n\n", parts)
            : "Aucune note de version disponible.";
    }

    internal static string ComponentLabel(string component) => component switch
    {
        "launcher" => "Launcher",
        "beacon" => "Beacon",
        _ => "RaidPilot"
    };
}
