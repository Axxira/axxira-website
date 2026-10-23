using System.Security.Cryptography;
using System.Text.Json;

namespace RaidPilot.Launcher;

/// <summary>
/// Durable, local journal for an Overlay/Beacon multi-component update.
/// A power loss between component swaps must not silently leave mixed builds.
/// The journal references only fixed installation folders and SHA-256 hashes
/// of pre-update executables; no user settings or absolute paths are stored.
/// </summary>
internal sealed record LauncherUpdateSnapshot(
    int SchemaVersion,
    bool UpdateRaidPilot,
    bool UpdateBeacon,
    string? PreviousRaidPilotSha256,
    string? PreviousBeaconSha256);

internal static class LauncherUpdateRecovery
{
    internal static string DefaultJournalPath => Path.Combine(
        LauncherSettingsStore.SettingsDirectory, "pending-install.json");

    internal static LauncherUpdateSnapshot Capture(
        bool updateRaidPilot, bool updateBeacon,
        string raidPilotExecutable, string beaconExecutable) => new(
            SchemaVersion: 1,
            UpdateRaidPilot: updateRaidPilot,
            UpdateBeacon: updateBeacon,
            PreviousRaidPilotSha256: updateRaidPilot
                ? CaptureHash(raidPilotExecutable) : null,
            PreviousBeaconSha256: updateBeacon
                ? CaptureHash(beaconExecutable) : null);

    internal static void Begin(string journalPath, LauncherUpdateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != 1 || (!snapshot.UpdateRaidPilot && !snapshot.UpdateBeacon))
            throw new ArgumentException("Invalid update journal.", nameof(snapshot));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(journalPath))!);
        var temporary = journalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
            File.Move(temporary, journalPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    /// <summary>
    /// Restores a previous complete version after an unclean termination.
    /// A component that never changed is left untouched, even if a stale
    /// .previous backup exists from a much older successful update.
    /// </summary>
    internal static bool Recover(
        string journalPath,
        string raidPilotDirectory,
        string beaconDirectory,
        out string result)
    {
        result = "Aucune installation interrompue.";
        if (!File.Exists(journalPath)) return true;
        LauncherUpdateSnapshot? snapshot;
        try
        {
            var data = File.ReadAllBytes(journalPath);
            if (data.Length is <= 0 or > 8192)
                throw new InvalidDataException("Journal size invalid.");
            snapshot = JsonSerializer.Deserialize<LauncherUpdateSnapshot>(data);
        }
        catch
        {
            result = "Journal de mise à jour illisible : réinstallation manuelle requise.";
            return false;
        }
        if (snapshot is not { SchemaVersion: 1 })
        {
            result = "Journal de mise à jour inconnu : intervention manuelle requise.";
            return false;
        }

        var raidOk = !snapshot.UpdateRaidPilot || RestoreOne(
            raidPilotDirectory, "RaidPilot.Overlay.exe",
            snapshot.PreviousRaidPilotSha256);
        var beaconOk = !snapshot.UpdateBeacon || RestoreOne(
            beaconDirectory, "RaidPilot.Beacon.exe",
            snapshot.PreviousBeaconSha256);
        if (!raidOk || !beaconOk)
        {
            result = "Récupération incomplète : "
                + (!raidOk ? "RaidPilot " : "")
                + (!beaconOk ? "Beacon " : "")
                + "— sauvegarde conservée pour contrôle.";
            return false;
        }
        try
        {
            File.Delete(journalPath);
            result = "Composants restaurés après interruption de mise à jour.";
            return true;
        }
        catch
        {
            result = "Restauration réussie, mais nettoyage du journal impossible.";
            return false;
        }
    }

    internal static void Complete(string journalPath)
    {
        if (File.Exists(journalPath)) File.Delete(journalPath);
    }

    private static bool RestoreOne(
        string directory, string executable, string? originalSha)
    {
        var activeExe = Path.Combine(directory, executable);
        if (string.IsNullOrWhiteSpace(originalSha))
        {
            // The component was absent before the interrupted transaction.
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                return true;
            }
            catch { return false; }
        }

        if (string.Equals(FileHash(activeExe), originalSha,
                StringComparison.OrdinalIgnoreCase))
            return true;

        var previousExe = Path.Combine(directory + ".previous", executable);
        if (!string.Equals(FileHash(previousExe), originalSha,
                StringComparison.OrdinalIgnoreCase))
            return false; // Never roll back to an unrelated older backup.
        return UpdateService.Rollback(directory, executable)
            && string.Equals(FileHash(activeExe), originalSha,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? CaptureHash(string executable)
    {
        if (!File.Exists(executable)) return null;
        return FileHash(executable) ?? throw new IOException(
            "Impossible de sécuriser la copie de la version actuelle : "
            + Path.GetFileName(executable));
    }

    private static string? FileHash(string executable)
    {
        if (!File.Exists(executable)) return null;
        try
        {
            using var file = File.OpenRead(executable);
            return Convert.ToHexString(SHA256.HashData(file));
        }
        catch
        {
            return null;
        }
    }
}
