using System.Diagnostics;
using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Opt-in periodic EFT screenshot anchors while the player is moving.
/// Uses only the user's existing screenshot binding and EFT's own filename
/// coordinates. Never reads EFT process memory or injects into its code.
/// The fixed minimum interval limits screenshot overlays and file churn.
/// </summary>
internal static class MobileExactAnchorPolicy
{
    internal const double MinimumIntervalSeconds = 4.0;

    internal static bool ShouldCapture(
        bool enabled,
        bool controlsActive,
        bool movementActive,
        bool aimingOrFiring,
        bool safeScreenshotBinding,
        bool hasScreenshotWatcher,
        bool hasExactAnchor,
        double secondsSinceExactAnchor,
        double secondsSinceLastAttempt)
    {
        if (!double.IsFinite(secondsSinceExactAnchor)
            || double.IsNaN(secondsSinceLastAttempt)
            || secondsSinceExactAnchor < 0
            || secondsSinceLastAttempt < 0)
            return false;

        return enabled && controlsActive && movementActive
            && !aimingOrFiring && safeScreenshotBinding
            && hasScreenshotWatcher && hasExactAnchor
            && secondsSinceExactAnchor >= MinimumIntervalSeconds
            && secondsSinceLastAttempt >= MinimumIntervalSeconds;
    }
}

internal static class MobileExactAnchorPreference
{
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidPilot", "mobile-exact-anchor.json");

    internal static bool Load(string? path = null)
    {
        try
        {
            var location = path ?? DefaultPath;
            var info = new FileInfo(location);
            if (!info.Exists || info.Length is <= 0 or > 4096) return false;
            using var data = JsonDocument.Parse(File.ReadAllBytes(location));
            var root = data.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("schemaVersion", out var schema)
                && schema.ValueKind == JsonValueKind.Number
                && schema.TryGetInt32(out var version) && version == 1
                && root.TryGetProperty("enabled", out var enabled)
                && enabled.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool TrySave(bool enabled, string? path = null)
    {
        string? temporary = null;
        try
        {
            var location = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(location))!);
            temporary = location + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var content = JsonSerializer.Serialize(new { schemaVersion = 1, enabled });
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, location, overwrite: true);
            temporary = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
