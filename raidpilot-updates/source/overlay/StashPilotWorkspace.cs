using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Local-only, bounded StashPilot workspace. No EFT profile, screenshots or
/// network identifiers are transmitted. Writes are atomic and validated.
/// </summary>
internal static class StashPilotWorkspace
{
    private const int MaxTextLength = 256_000;
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidPilot", "stashpilot-v1.json");

    internal static bool TrySave(string inventory, string requirements, string? path = null)
    {
        if (inventory.Length > MaxTextLength || requirements.Length > MaxTextLength)
            return false;
        var location = path ?? DefaultPath;
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(location))!);
            temporary = location + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new WorkspaceDocument(
                1, inventory, requirements));
            File.WriteAllBytes(temporary, bytes);
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

    internal static bool TryLoad(out string inventory, out string requirements,
        string? path = null)
    {
        inventory = requirements = "";
        try
        {
            var location = path ?? DefaultPath;
            var info = new FileInfo(location);
            if (!info.Exists || info.Length is <= 0 or > 600_000)
                return false;
            var record = JsonSerializer.Deserialize<WorkspaceDocument>(
                File.ReadAllBytes(location));
            if (record is not { SchemaVersion: 1 }
                || record.Inventory is null || record.Requirements is null
                || record.Inventory.Length > MaxTextLength
                || record.Requirements.Length > MaxTextLength)
                return false;
            inventory = record.Inventory;
            requirements = record.Requirements;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private sealed record WorkspaceDocument(
        int SchemaVersion, string Inventory, string Requirements);
}
