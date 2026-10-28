using System.Diagnostics;
using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Adaptive EFT screenshot anchors while the player is moving. Strong,
/// independently confirmed silent visual corrections can postpone an F8;
/// uncertainty or loss of confirmation restores the proven four-second
/// recovery cadence. No process-memory access or code injection.
/// </summary>
internal static class MobileExactAnchorPolicy
{
    internal const double MinimumIntervalSeconds = 4.0;
    internal const double VerifiedVisualIntervalSeconds = 8.0;
    internal const double StrongVisualIntervalSeconds = 12.0;

    // Never trust motion estimates or screenshots alone as an absolute fix.
    // Only recently APPLIED and independently corroborated visual keyframes
    // can justify asking Tarkov for fewer disruptive absolute F8 samples.
    // Invoke only after the correction has actually been applied. A lone
    // keyframe or unverified teammate contribution cannot extend the F8
    // interval, even if its matching confidence looks high.
    internal static bool IsTrustedSilentCorrection(
        VisualRelocalizationSource source,
        bool sharedAtlasSource,
        double confidence,
        double positionResidualMeters,
        double verticalResidualMeters,
        double yawResidualDegrees) =>
        !sharedAtlasSource
        && source == VisualRelocalizationSource.Consensus
        && double.IsFinite(confidence)
        && confidence >= 0.90 && confidence <= 1.0
        && double.IsFinite(positionResidualMeters)
        && positionResidualMeters is >= 0 and <= 2.0
        && double.IsFinite(verticalResidualMeters)
        && verticalResidualMeters is >= 0 and <= 1.25
        && double.IsFinite(yawResidualDegrees)
        && yawResidualDegrees is >= 0 and <= 15.0;

    internal static double RequiredIntervalSeconds(
        double uncertaintyMeters,
        LocalizationHealthState health,
        bool recentTrustedVisualCorrection,
        bool strongVisualCorroboration = true)
    {
        if (!double.IsFinite(uncertaintyMeters) || uncertaintyMeters < 0)
            return double.PositiveInfinity;

        if (!recentTrustedVisualCorrection
            || health is LocalizationHealthState.Orange or LocalizationHealthState.Red
            || uncertaintyMeters >= 2.25)
            return MinimumIntervalSeconds;

        return uncertaintyMeters < 1.25 && strongVisualCorroboration
            ? StrongVisualIntervalSeconds
            : VerifiedVisualIntervalSeconds;
    }

    internal static bool ShouldCapture(
        bool enabled,
        bool controlsActive,
        bool movementActive,
        bool aimingOrFiring,
        bool safeScreenshotBinding,
        bool hasScreenshotWatcher,
        bool hasExactAnchor,
        double secondsSinceExactAnchor,
        double secondsSinceLastAttempt,
        double positionUncertaintyMeters = 4.0,
        LocalizationHealthState health = LocalizationHealthState.Red,
        bool recentTrustedVisualCorrection = false,
        bool strongVisualCorroboration = true,
        double nativeEvidenceIntervalSeconds = MinimumIntervalSeconds)
    {
        if (!double.IsFinite(secondsSinceExactAnchor)
            || double.IsNaN(secondsSinceLastAttempt)
            || !double.IsFinite(positionUncertaintyMeters)
            || positionUncertaintyMeters < 0
            || secondsSinceExactAnchor < 0
            || secondsSinceLastAttempt < 0)
            return false;

        var interval = RequiredIntervalSeconds(positionUncertaintyMeters,
            health, recentTrustedVisualCorrection, strongVisualCorroboration);
        // An independently validated native-F8 history may bridge the gap
        // when rare simultaneous low/high visual consensus is unavailable.
        // It never upgrades the visual-only 12-second tier, and it cannot
        // override the live uncertainty or health guards.
        if (double.IsFinite(nativeEvidenceIntervalSeconds)
            && nativeEvidenceIntervalSeconds is >= 6.0 and <= 8.0
            && health is (LocalizationHealthState.Green or LocalizationHealthState.Yellow)
            && positionUncertaintyMeters < 1.35)
            interval = Math.Max(interval, nativeEvidenceIntervalSeconds);
        return enabled && controlsActive && movementActive
            && !aimingOrFiring && safeScreenshotBinding
            && hasScreenshotWatcher && hasExactAnchor
            && secondsSinceExactAnchor >= interval
            && secondsSinceLastAttempt >= interval;
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
            if (!info.Exists) return true; // Enable mobile reanchors on first launch; explicit opt-out persists.
            if (info.Length is <= 0 or > 4096) return false;
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
