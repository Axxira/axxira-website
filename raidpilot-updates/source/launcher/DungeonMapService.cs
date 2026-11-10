using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace RaidPilot.Launcher;

internal static class DungeonMapService
{
    internal static string BundlePath => Path.Combine(AppContext.BaseDirectory, "payload", "DungeonMapPilot");
    internal static readonly string[] Files = ["DungeonMapPilot.toc", "Core.lua", "Access.lua", "Locale.lua", "ReferenceMaps.lua", "Catalog.lua", "Model.lua", "RouteData.lua", "Routes.lua", "UI.lua", "RouteUI.lua", "Main.lua", "README.md", "LICENSE.txt", "LICENSE-OpenMap.txt"];

    internal static Version Validate(string folder)
    {
        ForeverPilotService.RejectLinkedPath(folder);
        foreach (var file in Files)
        {
            var p = ForeverPilotService.SafeChild(folder, file);
            ForeverPilotService.RejectLinkedPath(p);
            if (!File.Exists(p) || new FileInfo(p).Length > 4 * 1024 * 1024) throw new InvalidDataException("Paquet Cartes incomplet : " + file);
        }
        if (Directory.EnumerateFileSystemEntries(folder).Count() != Files.Length) throw new InvalidDataException("Fichier inattendu dans le paquet Cartes.");
        var toc = File.ReadAllText(Path.Combine(folder, "DungeonMapPilot.toc"));
        var modules = toc.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
        if (!modules.SetEquals(Files.Where(f => f.EndsWith(".lua", StringComparison.Ordinal)))) throw new InvalidDataException("Modules Cartes invalides.");
        if (!File.ReadAllText(Path.Combine(folder, "Access.lua")).Contains("A.distribution = \"raidpilot-test\"")) throw new InvalidDataException("Le launcher exige les cartes gratuites.");
        var value = Regex.Match(toc, @"(?m)^## Version: ([0-9]+\.[0-9]+\.[0-9]+)\s*$").Groups[1].Value;
        if (!Version.TryParse(value, out var version)) throw new InvalidDataException("Version Cartes invalide.");
        return version;
    }

    internal static string Install(string game, string bundle, Func<bool>? running = null)
    {
        running ??= ForeverPilotService.IsGameRunning;
        if (running()) throw new InvalidOperationException("Ferme WoW avant d'installer DungeonMapPilot.");
        game = ForeverPilotService.NormalizeGamePath(game) ?? throw new InvalidDataException("Installation WoW Forever introuvable.");
        var version = Validate(bundle);
        var target = Path.Combine(game, "Interface", "AddOns", "DungeonMapPilot");
        var backups = Path.Combine(game, "Interface", "ForeverPilotBackups");
        ForeverPilotService.RejectLinkedPath(target); ForeverPilotService.RejectLinkedPath(backups);
        var oldToc = Path.Combine(target, "DungeonMapPilot.toc");
        if (File.Exists(oldToc))
        {
            var old = Regex.Match(File.ReadAllText(oldToc), @"(?m)^## Version: ([0-9]+\.[0-9]+\.[0-9]+)\s*$").Groups[1].Value;
            if (Version.TryParse(old, out var installed) && installed > version) throw new InvalidOperationException("Une version Cartes plus récente est déjà installée.");
            // Preserve unknown user files by refusing an ambiguous replacement.
            foreach (var entry in Directory.EnumerateFileSystemEntries(target))
            {
                ForeverPilotService.RejectLinkedPath(entry);
                if (!Files.Contains(Path.GetFileName(entry), StringComparer.Ordinal)) throw new InvalidDataException("Fichier personnel dans DungeonMapPilot : installation manuelle nécessaire.");
            }
        }
        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var stage = Path.Combine(backups, "staging-DungeonMapPilot-" + id);
        var backup = Path.Combine(backups, "DungeonMapPilot-" + id);
        Directory.CreateDirectory(stage);
        foreach (var file in Files)
        {
            var from = Path.Combine(bundle, file); var to = Path.Combine(stage, file);
            File.Copy(from, to, false);
            using var a = File.OpenRead(from); using var b = File.OpenRead(to);
            if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new IOException("Copie Cartes non vérifiée.");
        }
        Validate(stage);
        if (running()) throw new InvalidOperationException("WoW est ouvert : installation reportée.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        bool moved = false, installedNew = false;
        try
        {
            if (Directory.Exists(target)) { Directory.Move(target, backup); moved = true; }
            Directory.Move(stage, target); installedNew = true; Validate(target);
        }
        catch
        {
            if (installedNew) Directory.Move(target, stage);
            if (moved) Directory.Move(backup, target);
            throw;
        }
        return moved ? backup : "";
    }
}
