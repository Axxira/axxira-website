using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaidPilot.Launcher;

internal sealed record UpdateComponent
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; init; }

    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; init; }
}

internal sealed record UpdateManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "stable";

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; init; }

    [JsonPropertyName("raidPilot")]
    public UpdateComponent? RaidPilot { get; init; }

    [JsonPropertyName("beacon")]
    public UpdateComponent? Beacon { get; init; }

    [JsonPropertyName("launcher")]
    public UpdateComponent? Launcher { get; init; }

    [JsonPropertyName("releaseHistory")]
    public List<ReleaseHistoryEntry>? ReleaseHistory { get; init; }
}

internal sealed record LauncherSettings(string ManifestUrl, string Channel, bool AutoInstallWhenIdle = false)
{
    internal const string StableManifestUrl =
        "https://raw.githubusercontent.com/Axxira/axxira-website/raidpilot-updates/raidpilot-updates/update-manifest.json";

    internal static LauncherSettings Default => new(StableManifestUrl, "stable");
}

internal static class LauncherSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    internal static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidPilot Launcher");

    internal static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    internal static LauncherSettings Load() => LoadFromPath(SettingsPath);

    internal static LauncherSettings LoadFromPath(string settingsPath)
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                var settings = JsonSerializer.Deserialize<LauncherSettings>(
                    File.ReadAllText(settingsPath), JsonOptions);
                if (settings is not null)
                {
                    // An explicit empty feed is an intentional OFF switch.
                    // Never silently restore Stable when the user disabled it.
                    return Sanitize(settings);
                }
            }

            var bundledFeed = Path.Combine(AppContext.BaseDirectory, "update-feed.txt");
            if (File.Exists(bundledFeed))
            {
                var feed = File.ReadAllText(bundledFeed).Trim();
                if (UpdateService.IsAllowedManifestLocation(feed))
                {
                    return new LauncherSettings(feed, "stable");
                }
            }
        }
        catch
        {
        }
        return LauncherSettings.Default;
    }

    internal static bool Save(LauncherSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var sanitized = Sanitize(settings);
            var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(sanitized, JsonOptions));
            File.Move(temporary, SettingsPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static LauncherSettings Sanitize(LauncherSettings settings)
    {
        var url = settings.ManifestUrl?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(url)
            && !UpdateService.IsAllowedManifestLocation(url))
        {
            url = string.Empty;
        }
        var channel = string.Equals(
            settings.Channel,
            "beta",
            StringComparison.OrdinalIgnoreCase)
            ? "beta"
            : "stable";
        return new LauncherSettings(url, channel, settings.AutoInstallWhenIdle);
    }
}

internal static class LauncherPaths
{
    internal static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidPilot Suite");

    internal static string Apps => Path.Combine(Root, "Apps");
    internal static string Downloads => Path.Combine(Root, "Downloads");
    internal static string RaidPilot => Path.Combine(Apps, "RaidPilot");
    internal static string Beacon => Path.Combine(Apps, "Beacon");
    internal static string RaidPilotExe => Path.Combine(RaidPilot, "RaidPilot.Overlay.exe");
    internal static string BeaconExe => Path.Combine(Beacon, "RaidPilot.Beacon.exe");
    internal static string BundledRaidPilot => Path.Combine(AppContext.BaseDirectory, "payload", "RaidPilot");
    internal static string BundledBeacon => Path.Combine(AppContext.BaseDirectory, "payload", "Beacon");
}
