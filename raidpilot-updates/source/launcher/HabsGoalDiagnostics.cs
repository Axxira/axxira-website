namespace RaidPilot.Launcher;

// Local, bounded troubleshooting only: NHL events and companion outcomes, never game/player data.
internal static class HabsGoalDiagnostics
{
    internal const int MaxBytes = 262_144;
    internal static string LogPath => Path.Combine(LauncherSettingsStore.SettingsDirectory, "habs-goals.log");

    internal static void Write(string message, string? path = null)
    {
        try
        {
            path ??= LogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length >= MaxBytes)
                File.Move(path, path + ".previous", true);
            var line = message.Replace('\r', ' ').Replace('\n', ' ');
            if (line.Length > 1000) line = line[..1000];
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { /* Diagnostics must never stop polling or the effect. */ }
    }
}
