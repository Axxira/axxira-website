namespace RaidPilot.Launcher;

/// <summary>
/// Check final packaged artifacts, not just compilable source. Used by the
/// Windows release job after Overlay and Beacon are embedded in the Launcher.
/// A failed package check blocks public distribution.
/// </summary>
internal static class LauncherPackageVerifier
{
    internal static bool Verify(string root, out string problem)
    {
        problem = "";
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            problem = "Dossier Launcher absent.";
            return false;
        }

        var required = new[]
        {
            "RaidPilot.Launcher.exe",
            "RaidPilot.Launcher.dll",
            "update-feed.txt",
            "release-history.json",
            Path.Combine("payload", "RaidPilot", "RaidPilot.Overlay.exe"),
            Path.Combine("payload", "RaidPilot", "RaidPilot.Overlay.dll"),
            Path.Combine("payload", "RaidPilot", "RaidPilot.Capture.Native.dll"),
            Path.Combine("payload", "RaidPilot", "LocalMap", "index.html"),
            Path.Combine("payload", "RaidPilot", "LocalMap", "assets", "manifest.json"),
            Path.Combine("payload", "Beacon", "RaidPilot.Beacon.exe"),
            Path.Combine("payload", "Beacon", "RaidPilot.Beacon.dll"),
            Path.Combine("payload", "Beacon", "RaidPilot.Capture.Native.dll"),
        };

        foreach (var relative in required)
        {
            if (!File.Exists(Path.Combine(root, relative)))
            {
                problem = "Fichier manquant du package : " + relative;
                return false;
            }
        }
        var forbidden = new[]
        {
            Path.Combine(root, "UserData"),
            Path.Combine(root, "payload", "RaidPilot", "UserData"),
            Path.Combine(root, "payload", "Beacon", "UserData"),
        };
        if (forbidden.Any(Directory.Exists))
        {
            problem = "Des données personnelles se trouvent dans le package.";
            return false;
        }
        try
        {
            var feed = File.ReadAllText(Path.Combine(root, "update-feed.txt")).Trim();
            if (!string.Equals(feed, LauncherSettings.StableManifestUrl,
                    StringComparison.OrdinalIgnoreCase))
            {
                problem = "Source Stable intégrée incohérente.";
                return false;
            }
            var entries = ReleaseHistoryCatalog.ReadBundled(
                Path.Combine(root, "release-history.json"));
            if (entries.Count == 0)
            {
                problem = "Notes de version manquantes ou illisibles.";
                return false;
            }
            var latest = entries[0];
            var actualLauncher = UpdateService.GetInstalledVersion(
                Path.Combine(root, "RaidPilot.Launcher.exe"));
            var actualOverlay = UpdateService.GetInstalledVersion(
                Path.Combine(root, "payload", "RaidPilot", "RaidPilot.Overlay.exe"));
            var actualBeacon = UpdateService.GetInstalledVersion(
                Path.Combine(root, "payload", "Beacon", "RaidPilot.Beacon.exe"));
            if (!SameCoreVersion(actualLauncher, latest.LauncherVersion)
                || !SameCoreVersion(actualOverlay, latest.RaidPilotVersion)
                || !SameCoreVersion(actualBeacon, latest.BeaconVersion))
            {
                problem = "Versions du package différentes des notes de version.";
                return false;
            }
        }
        catch (Exception error)
        {
            problem = "Impossible de valider la publication : " + error.GetType().Name;
            return false;
        }
        return true;
    }

    private static bool SameCoreVersion(string? installed, string? claimed)
    {
        static string Core(string? value) =>
            (value ?? "").Split(new[] { '+', '-' }, 2)[0];
        return Version.TryParse(Core(installed), out var first)
            && Version.TryParse(Core(claimed), out var second)
            && first == second;
    }
}
