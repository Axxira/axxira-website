using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace RaidPilot.Launcher;

/// <summary>Downloads the author's unmodified release. Never bundled or mirrored.</summary>
internal static class ChangeLayerService
{
    internal const string PageUrl = "https://www.curseforge.com/wow/addons/changelayer";
    internal const string DownloadUrl = "https://mediafilez.forgecdn.net/files/9480/828/ChangeLayer.zip";
    internal const string ArchiveHash = "DBC8EEFCE95D19D0D161AABE3A12BB22B36234D4104C72BE14EBDE678D4E7267";
    internal const string Version = "1.1.4";
    private static readonly string[] Files = ["Bindings.xml", "ChangeLayer.toc", "Core.lua", "Network.lua", "Options.lua", "Plate.lua"];

    internal static string? InstalledVersion(string? game)
    {
        if (game is null) return null;
        try { return ValidateFolder(Path.Combine(game, "Interface", "AddOns", "ChangeLayer")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }

    private static string ValidateFolder(string folder)
    {
        ForeverPilotService.RejectLinkedPath(folder);
        foreach (var name in Files)
        {
            var file = new FileInfo(Path.Combine(folder, name));
            ForeverPilotService.RejectLinkedPath(file.FullName);
            if (!file.Exists || file.Length == 0 || file.Length > 256 * 1024)
                throw new InvalidDataException("ChangeLayer incomplet : " + name);
        }
        var lines = File.ReadAllLines(Path.Combine(folder, "ChangeLayer.toc"));
        var version = lines.FirstOrDefault(l => l.StartsWith("## Version:"))?[11..].Trim();
        if (!System.Version.TryParse(version, out _) || !lines.Any(l => l.StartsWith("## Interface:") && l[13..].Split(',').Any(v => v.Trim() == "16001")))
            throw new InvalidDataException("ChangeLayer doit être l'édition Forever.");
        foreach (var module in lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            var path = ForeverPilotService.SafeChild(folder, module);
            ForeverPilotService.RejectLinkedPath(path);
            if (!File.Exists(path)) throw new InvalidDataException("Module ChangeLayer absent : " + module);
        }
        return version!;
    }

    internal static async Task<string> DownloadAndInstall(string game)
    {
        if (ForeverPilotService.IsGameRunning()) throw new InvalidOperationException("Ferme WoW avant d'installer ChangeLayer.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45), MaxResponseContentBufferSize = 1024 * 1024 };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ForeverPilot-ChangeLayerInstaller/1.0");
        var archive = await http.GetByteArrayAsync(DownloadUrl);
        return InstallArchive(game, archive, ArchiveHash, ForeverPilotService.IsGameRunning);
    }

    internal static string InstallArchive(string game, byte[] archive, string expectedHash, Func<bool> gameRunning)
    {
        if (gameRunning()) throw new InvalidOperationException("Ferme WoW avant d'installer ChangeLayer.");
        game = ForeverPilotService.NormalizeGamePath(game) ?? throw new InvalidDataException("Installation WoW Forever introuvable.");
        ForeverPilotService.RejectLinkedPath(game);
        if (archive.Length > 1024 * 1024 || !Convert.ToHexString(SHA256.HashData(archive)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Téléchargement ChangeLayer invalide. Aucun fichier installé.");
        var target = Path.Combine(game, "Interface", "AddOns", "ChangeLayer");
        ForeverPilotService.RejectLinkedPath(target);
        var installed = InstalledVersion(game);
        if (installed is not null && System.Version.Parse(installed) >= System.Version.Parse(Version))
            return "ChangeLayer " + installed + " est déjà installé. Active-le dans WoW.";
        var toc = Path.Combine(target, "ChangeLayer.toc");
        if (File.Exists(toc))
        {
            var rawVersion = File.ReadLines(toc).FirstOrDefault(l => l.StartsWith("## Version:"))?[11..].Trim();
            if (System.Version.TryParse(rawVersion, out var newer) && newer > System.Version.Parse(Version))
                throw new InvalidDataException("ChangeLayer plus récent mais incomplet : répare-le depuis CurseForge.");
        }
        var backups = Path.Combine(game, "Interface", "ForeverPilotBackups");
        ForeverPilotService.RejectLinkedPath(backups);
        var id = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(backups, "staging-ChangeLayer-" + id);
        var backup = Path.Combine(backups, "ChangeLayer-" + id);
        Directory.CreateDirectory(stage);
        using (var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (name == "ChangeLayer/" && entry.Length == 0) continue;
                if (!name.StartsWith("ChangeLayer/", StringComparison.Ordinal) || !Files.Contains(name[12..], StringComparer.Ordinal) ||
                    !seen.Add(name) || entry.Length == 0 || entry.Length > 256 * 1024)
                    throw new InvalidDataException("Fichier inattendu dans ChangeLayer : " + name);
                var destination = ForeverPilotService.SafeChild(stage, name[12..]);
                entry.ExtractToFile(destination, overwrite: false);
            }
        }
        if (ValidateFolder(stage) != Version) throw new InvalidDataException("Version ChangeLayer inattendue.");
        if (gameRunning()) throw new InvalidOperationException("WoW vient de démarrer. Installation reportée.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var moved = false;
        try
        {
            if (Directory.Exists(target)) { Directory.Move(target, backup); moved = true; }
            Directory.Move(stage, target);
            ValidateFolder(target);
        }
        catch
        {
            if (Directory.Exists(target) && !Directory.Exists(stage)) Directory.Move(target, stage);
            if (moved) Directory.Move(backup, target);
            throw;
        }
        return "ChangeLayer " + Version + " installé depuis CurseForge. Active-le puis relance WoW via Battle.net.";
    }
}
