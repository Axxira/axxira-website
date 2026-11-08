using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace RaidPilot.Launcher;

internal sealed record ForeverPilotStatus(string? GamePath, string? Version, bool DingInstalled,
    string? BundledVersion, string? BattleNetPath, bool GameRunning, string? Problem)
{
    internal bool Ready => GamePath is not null && Version is not null && DingInstalled && Problem is null;
    internal bool UpdateAvailable => BundledVersion is not null &&
        (Version is null || System.Version.Parse(BundledVersion) > System.Version.Parse(Version));
}

/// <summary>Manages only our addon. Never writes WTF, Ding or the legacy ForeverPilot bridge.</summary>
internal static class ForeverPilotService
{
    internal const string AddonName = "ForeverPilot_Leveling";
    internal static string BundlePath => Path.Combine(AppContext.BaseDirectory, "payload", AddonName);
    private static string SettingsPath => Path.Combine(LauncherSettingsStore.SettingsDirectory, "foreverpilot.json");

    internal static ForeverPilotStatus Inspect()
    {
        string? selected = null;
        try
        {
            if (File.Exists(SettingsPath))
                selected = JsonSerializer.Deserialize<string>(File.ReadAllText(SettingsPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        var defaults = new[] { selected,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "World of Warcraft", "_forever_"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "World of Warcraft", "_forever_") };
        // A saved choice must not silently switch to a different installation if its disk is absent.
        var game = selected is not null ? NormalizeGamePath(selected) : defaults.Select(NormalizeGamePath).FirstOrDefault(p => p is not null);
        return InspectAt(game, BundlePath, FindBattleNet(), IsGameRunning());
    }

    internal static ForeverPilotStatus InspectAt(string? game, string bundle, string? battleNet, bool running)
    {
        var addon = game is null ? null : Path.Combine(game, "Interface", "AddOns", AddonName);
        var version = addon is null ? null : ReadVersion(addon);
        string? problem = null;
        if (addon is not null && Directory.Exists(addon))
        {
            try { ValidateAddon(addon); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            { problem = "Installation incomplète : " + e.Message; }
        }
        var ding = game is not null && File.Exists(Path.Combine(game, "Interface", "AddOns", "Ding", "Ding.toc"));
        string? bundled = null;
        try { ValidateAddon(bundle); bundled = ReadVersion(bundle); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
        return new(game, version, ding, bundled, battleNet, running, problem);
    }

    internal static string? NormalizeGamePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim());
            if (!string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(full)), "_forever_", StringComparison.OrdinalIgnoreCase))
                full = Path.Combine(full, "_forever_");
            return File.Exists(Path.Combine(full, "Wow.exe")) ? Path.TrimEndingDirectorySeparator(full) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    internal static void SaveGamePath(string path)
    {
        var normalized = NormalizeGamePath(path) ?? throw new InvalidDataException("Choisis le dossier _forever_ contenant Wow.exe.");
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(normalized));
        File.Move(temporary, SettingsPath, overwrite: true);
    }

    internal static string? ReadVersion(string folder)
    {
        try
        {
            var file = new FileInfo(Path.Combine(folder, AddonName + ".toc"));
            if (!file.Exists || file.Length > 65536) return null;
            var value = File.ReadLines(file.FullName).FirstOrDefault(l => l.TrimStart().StartsWith("## Version:", StringComparison.OrdinalIgnoreCase));
            var version = value?[(value.IndexOf(':') + 1)..].Trim();
            return Version.TryParse(version, out var parsed) ? parsed.ToString() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static void ValidateAddon(string folder)
    {
        RejectLinkedPath(folder);
        if (ReadVersion(folder) is null) throw new InvalidDataException("Version de ForeverPilot introuvable.");
        var lines = File.ReadAllLines(Path.Combine(folder, AddonName + ".toc"));
        var modules = lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();
        if (modules.Length == 0) throw new InvalidDataException("Aucun module dans le fichier TOC.");
        foreach (var relative in modules)
        {
            var path = SafeChild(folder, relative);
            RejectLinkedPath(path);
            if (!File.Exists(path)) throw new InvalidDataException("Module manquant : " + relative);
        }
    }

    internal static string SafeChild(string root, string relative)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(prefix, relative));
        if (Path.IsPathRooted(relative) || relative.Contains(':') || !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chemin d'addon invalide.");
        return full;
    }

    private static void RejectLinkedPath(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Les dossiers liés ne sont pas pris en charge.");
    }

    internal static bool IsGameRunning()
    {
        foreach (var name in new[] { "Wow", "WowClassic", "WowClassicT" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }

    internal static string? FindBattleNet()
    {
        var candidates = new List<string?>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var key in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Battle.net.exe", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\Battle.net.exe" })
        {
            try { using var app = hive.OpenSubKey(key); candidates.Add((app?.GetValue(null) as string)?.Trim('"')); }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
        {
            candidates.Add(Path.Combine(root, "Battle.net", "Battle.net Launcher.exe"));
            candidates.Add(Path.Combine(root, "Battle.net", "Battle.net.exe"));
        }
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p) &&
            Path.GetFileName(p) is "Battle.net.exe" or "Battle.net Launcher.exe");
    }

    internal static string Install(string game, string bundle, Func<bool>? gameRunning = null)
    {
        if ((gameRunning ?? IsGameRunning)()) throw new InvalidOperationException("Ferme World of Warcraft avant de mettre l'addon à jour.");
        game = NormalizeGamePath(game) ?? throw new InvalidDataException("Installation WoW Forever introuvable.");
        RejectLinkedPath(game);
        ValidateAddon(bundle);
        var target = Path.Combine(game, "Interface", "AddOns", AddonName);
        RejectLinkedPath(target);
        var existing = ReadVersion(target);
        if (existing is not null && Version.Parse(existing) > Version.Parse(ReadVersion(bundle)!))
            throw new InvalidOperationException("La version installée est plus récente que celle du launcher.");
        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var backups = Path.Combine(game, "Interface", "ForeverPilotBackups");
        RejectLinkedPath(backups);
        var stage = Path.Combine(backups, "staging-" + id);
        var backup = Path.Combine(backups, (existing ?? "installation") + "-" + id);
        CopyVerified(bundle, stage);
        ValidateAddon(stage);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Recheck immediately before switching folders: WoW may have started during staging.
        if ((gameRunning ?? IsGameRunning)()) throw new InvalidOperationException("WoW est ouvert. Installation reportée.");
        var hadOld = Directory.Exists(target);
        if (hadOld) Directory.Move(target, backup);
        try { Directory.Move(stage, target); }
        catch
        {
            if (hadOld && !Directory.Exists(target)) Directory.Move(backup, target);
            throw;
        }
        return hadOld ? backup : "";
    }

    private static void CopyVerified(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            RejectLinkedPath(entry);
            var dest = SafeChild(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry)) { CopyVerified(entry, dest); continue; }
            var info = new FileInfo(entry);
            if (info.Length > 16 * 1024 * 1024 || !new[] { ".lua", ".toc", ".tga", ".md", ".txt" }.Contains(info.Extension.ToLowerInvariant()))
                throw new InvalidDataException("Fichier inattendu dans l'addon : " + info.Name);
            File.Copy(entry, dest, overwrite: false);
            using var original = File.OpenRead(entry);
            using var copied = File.OpenRead(dest);
            if (!SHA256.HashData(original).SequenceEqual(SHA256.HashData(copied)))
                throw new IOException("La copie de l'addon n'a pas pu être vérifiée.");
        }
    }
}
