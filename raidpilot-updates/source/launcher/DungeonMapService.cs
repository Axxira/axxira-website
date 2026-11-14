using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace RaidPilot.Launcher;

internal static class DungeonMapService
{
    internal static string BundlePath => Path.Combine(AppContext.BaseDirectory, "payload", "DungeonMapPilot");
    internal static readonly string[] Files = [
        "Access.lua", "BossNotebook.lua", "BossNotebookLocale.lua", "BossNotebookUI.lua", "Catalog.lua",
        "CatalogUX.lua", "ClientRoster.lua", "Core.lua", "DATA-SOURCES.md", "DungeonGuides.lua",
        "DungeonMapPilot.toc", "EncounterSearch.lua", "EncounterSearchUI.lua", "ForeverGuides.lua", "GUIDES-STATUS.md",
        "GuideLocale.lua", "LICENSE-OpenMap.txt", "LICENSE.txt", "Locale.lua", "Locales/deDE.lua",
        "Locales/esES.lua", "Locales/esMX.lua", "Locales/itIT.lua", "Locales/koKR.lua", "Locales/ptBR.lua",
        "Locales/ruRU.lua", "Locales/zhCN.lua", "Locales/zhTW.lua", "Main.lua", "MinimapLauncher.lua",
        "Model.lua", "PilotMinimap.lua", "PreflightNotes.lua", "README.md", "RELEASE-NOTES.md",
        "ReferenceMaps.lua", "RosterLocale.lua", "RouteData.lua", "RouteLocale.lua", "RouteUI.lua",
        "Routes.lua", "SearchLocale.lua", "SuiteBridge.lua", "Support.lua", "SupportConfig.lua",
        "UI.lua"];
    private const string VersionPattern = @"(?m)^## Version: ([0-9]+\.[0-9]+\.[0-9]+)(?:-[A-Za-z0-9.]+)?\s*$";

    private static HashSet<string> KnownFiles(string folder)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.TryPop(out var directory))
        {
            ForeverPilotService.RejectLinkedPath(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                // Check before traversing a directory, including junctions/symlinks.
                ForeverPilotService.RejectLinkedPath(entry);
                var relative = Path.GetRelativePath(folder, entry).Replace('\\', '/');
                if (Directory.Exists(entry))
                {
                    if (!Files.Any(f => f.StartsWith(relative + "/", StringComparison.Ordinal)))
                        throw new InvalidDataException("Dossier inattendu dans le paquet Cartes : " + relative);
                    pending.Push(entry);
                }
                else
                {
                    if (!Files.Contains(relative, StringComparer.Ordinal))
                        throw new InvalidDataException("Fichier personnel ou inattendu dans DungeonMapPilot : " + relative);
                    found.Add(relative);
                }
            }
        }
        return found;
    }

    internal static Version Validate(string folder)
    {
        ForeverPilotService.RejectLinkedPath(folder);
        foreach (var file in Files)
        {
            var p = ForeverPilotService.SafeChild(folder, file);
            ForeverPilotService.RejectLinkedPath(p);
            if (!File.Exists(p) || new FileInfo(p).Length > 4 * 1024 * 1024) throw new InvalidDataException("Paquet Cartes incomplet : " + file);
        }
        if (!KnownFiles(folder).SetEquals(Files)) throw new InvalidDataException("Fichiers Cartes incomplets.");
        var toc = File.ReadAllText(Path.Combine(folder, "DungeonMapPilot.toc"));
        var modules = toc.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
        if (!modules.SetEquals(Files.Where(f => f.EndsWith(".lua", StringComparison.Ordinal)))) throw new InvalidDataException("Modules Cartes invalides.");
        if (!File.ReadAllText(Path.Combine(folder, "Access.lua")).Contains("A.distribution = \"raidpilot-test\"")) throw new InvalidDataException("Le launcher exige les cartes gratuites.");
        var value = Regex.Match(toc, VersionPattern).Groups[1].Value;
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
        // Older complete or partial installations may contain a subset of known files.
        // Refuse unknown files even when the previous TOC is missing.
        if (Directory.Exists(target)) KnownFiles(target);
        var oldToc = Path.Combine(target, "DungeonMapPilot.toc");
        if (File.Exists(oldToc))
        {
            var old = Regex.Match(File.ReadAllText(oldToc), VersionPattern).Groups[1].Value;
            if (Version.TryParse(old, out var installed) && installed > version) throw new InvalidOperationException("Une version Cartes plus récente est déjà installée.");
        }
        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var stage = Path.Combine(backups, "staging-DungeonMapPilot-" + id);
        var backup = Path.Combine(backups, "DungeonMapPilot-" + id);
        Directory.CreateDirectory(stage);
        foreach (var file in Files)
        {
            var from = Path.Combine(bundle, file); var to = Path.Combine(stage, file);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
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
