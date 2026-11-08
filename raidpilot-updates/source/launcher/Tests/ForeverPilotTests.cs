using RaidPilot.Launcher;

internal static class ForeverPilotTests
{
    internal static void VerifyPublishedPackage(string launcherRoot)
    {
        var addon = Path.Combine(Path.GetFullPath(launcherRoot), "payload", ForeverPilotService.AddonName);
        var ding = Path.Combine(Path.GetFullPath(launcherRoot), "payload", "Ding");
        ForeverPilotService.ValidateAddon(addon);
        ForeverPilotService.ValidateDing(ding, requireLicense: true);
        WithFixture((game, _) =>
        {
            Directory.Delete(Path.Combine(game, "Interface", "AddOns", "Ding"), true);
            ForeverPilotService.Install(game, addon, () => false, ding);
            Require(ForeverPilotService.InspectAt(game, addon, null, false).Ready, "installation du package réel non prête");
            int verified = 0;
            foreach (var source in new[] { addon, ding })
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var installed = Path.Combine(game, "Interface", "AddOns", Path.GetFileName(source), relative);
                using var first = File.OpenRead(file);
                using var second = File.OpenRead(installed);
                Require(System.Security.Cryptography.SHA256.HashData(first).SequenceEqual(System.Security.Cryptography.SHA256.HashData(second)),
                    "copie différente : " + relative);
                verified++;
            }
            Console.WriteLine($"Fresh-PC package: {verified} addon files installed and hash-verified, Ding + ForeverPilot ready.");
        });
    }

    internal static void Run(Action<string, Action> run)
    {
        run("ForeverPilot : détecter la bonne édition et ignorer l'ancien bridge", () => WithFixture((game, bundle) =>
        {
            Require(ForeverPilotService.NormalizeGamePath(Path.GetDirectoryName(game)) == game, "root WoW non reconnu");
            Require(ForeverPilotService.NormalizeGamePath(game) == game, "_forever_ non reconnu");
            var retail = Path.Combine(Path.GetDirectoryName(game)!, "_retail_");
            Directory.CreateDirectory(retail); File.WriteAllText(Path.Combine(retail, "Wow.exe"), "");
            Require(ForeverPilotService.NormalizeGamePath(retail) is null, "Retail accepté");
            var state = ForeverPilotService.InspectAt(game, bundle, null, false);
            Require(state.Version is null && !state.Ready && state.DingInstalled, "bridge confondu avec addon leveling");
        }));
        run("ForeverPilot : installation complète, Ding et bridge conservés", () => WithFixture((game, bundle) =>
        {
            var backup = ForeverPilotService.Install(game, bundle, () => false);
            var state = ForeverPilotService.InspectAt(game, bundle, null, false);
            Require(state.Ready && state.Version == "1.4.1" && !state.UpdateAvailable && backup == "", "installation non reconnue");
            Require(File.ReadAllText(Path.Combine(game, "Interface", "AddOns", "ForeverPilot", "bridge.lua")) == "keep bridge", "bridge modifié");
            Require(File.ReadAllText(Path.Combine(game, "WTF", "settings.lua")) == "keep settings", "WTF modifié");
        }));
        run("ForeverPilot : ancienne version sauvegardée avant mise à jour", () => WithFixture((game, bundle) =>
        {
            var target = Path.Combine(game, "Interface", "AddOns", ForeverPilotService.AddonName);
            WriteAddon(target, "1.3.0");
            Require(ForeverPilotService.InspectAt(game, bundle, null, false).UpdateAvailable, "update non détectée");
            var backup = ForeverPilotService.Install(game, bundle, () => false);
            Require(ForeverPilotService.ReadVersion(backup) == "1.3.0", "sauvegarde absente");
            Require(ForeverPilotService.ReadVersion(target) == "1.4.1", "update non installée");
        }));
        run("ForeverPilot : aucune rétrogradation", () => WithFixture((game, bundle) =>
        {
            var target = Path.Combine(game, "Interface", "AddOns", ForeverPilotService.AddonName);
            WriteAddon(target, "1.5.0");
            Reject(() => ForeverPilotService.Install(game, bundle, () => false));
            Require(ForeverPilotService.ReadVersion(target) == "1.5.0", "downgrade effectué");
        }));
        run("ForeverPilot : aucun remplacement pendant le jeu", () => WithFixture((game, bundle) =>
        {
            Reject(() => ForeverPilotService.Install(game, bundle, () => true));
            Require(!Directory.Exists(Path.Combine(game, "Interface", "AddOns", ForeverPilotService.AddonName)), "installation pendant WoW");
            var calls = 0;
            Reject(() => ForeverPilotService.Install(game, bundle, () => ++calls > 1));
            Require(!Directory.Exists(Path.Combine(game, "Interface", "AddOns", ForeverPilotService.AddonName)), "WoW ouvert pendant staging ignoré");
        }));
        run("ForeverPilot : module absent signale une réparation, pas une installation prête", () => WithFixture((game, bundle) =>
        {
            var target = Path.Combine(game, "Interface", "AddOns", ForeverPilotService.AddonName);
            WriteAddon(target, "1.4.1"); File.Delete(Path.Combine(target, "Main.lua"));
            var state = ForeverPilotService.InspectAt(game, bundle, null, false);
            Require(!state.Ready && state.Problem is not null, "addon cassé déclaré prêt");
        }));
        run("ForeverPilot : package cassé laisse l'installation intacte", () => WithFixture((game, bundle) =>
        {
            var target = Path.Combine(game, "Interface", "AddOns", ForeverPilotService.AddonName);
            WriteAddon(target, "1.3.0"); File.Delete(Path.Combine(bundle, "Main.lua"));
            Reject(() => ForeverPilotService.Install(game, bundle, () => false));
            Require(ForeverPilotService.ReadVersion(target) == "1.3.0", "installation détruite");
        }));
        run("ForeverPilot : refuser les chemins sortants et les exécutables dans l'addon", () => WithFixture((game, bundle) =>
        {
            Reject(() => ForeverPilotService.SafeChild(bundle, "../bridge.lua"));
            Reject(() => ForeverPilotService.SafeChild(bundle, "C:\\bridge.lua"));
            File.WriteAllText(Path.Combine(bundle, "malicious.exe"), "not addon content");
            Reject(() => ForeverPilotService.Install(game, bundle, () => false));
        }));
        run("ForeverPilot : dépendance absente explicitement signalée", () => WithFixture((game, bundle) =>
        {
            ForeverPilotService.Install(game, bundle, () => false);
            File.Delete(Path.Combine(game, "Interface", "AddOns", "Ding", "Ding.toc"));
            var state = ForeverPilotService.InspectAt(game, bundle, null, false);
            Require(!state.DingInstalled && !state.Ready && state.Version == "1.4.1", "Ding absent masqué");
        }));
        run("ForeverPilot : installation neuve fournit les deux addons sans Ding préalable", () => WithFixture((game, bundle) =>
        {
            var ding = Path.Combine(game, "Interface", "AddOns", "Ding");
            Directory.Delete(ding, true);
            ForeverPilotService.Install(game, bundle, () => false, Path.Combine(Path.GetDirectoryName(bundle)!, "Ding"));
            Require(ForeverPilotService.InspectAt(game, bundle, null, false).Ready, "installation neuve incomplète");
            Require(File.Exists(Path.Combine(ding, "LICENSE")), "licence perdue");
            Require(File.ReadAllText(Path.Combine(game, "Interface", "AddOns", "ForeverPilot", "bridge.lua")) == "keep bridge", "bridge changé");
        }));
        run("ForeverPilot : Ding incomplet réparé avec sauvegarde", () => WithFixture((game, bundle) =>
        {
            var ding = Path.Combine(game, "Interface", "AddOns", "Ding");
            File.Delete(Path.Combine(ding, "Core", "Engine.lua"));
            File.WriteAllText(Path.Combine(ding, "custom.txt"), "keep backup");
            Require(!ForeverPilotService.InspectAt(game, bundle, null, false).DingInstalled, "TOC seul considéré complet");
            var backup = ForeverPilotService.Install(game, bundle, () => false, Path.Combine(Path.GetDirectoryName(bundle)!, "Ding"));
            Require(ForeverPilotService.IsDingComplete(ding), "Ding non réparé");
            Require(File.ReadAllText(Path.Combine(backup, "custom.txt")) == "keep backup", "copie précédente perdue");
        }));
        run("ForeverPilot : Ding existant valide conservé sans changement", () => WithFixture((game, bundle) =>
        {
            var file = Path.Combine(game, "Interface", "AddOns", "Ding", "Core", "Ding.lua");
            File.WriteAllText(file, "-- newer or customized Ding");
            ForeverPilotService.Install(game, bundle, () => false);
            Require(File.ReadAllText(file) == "-- newer or customized Ding", "Ding existant écrasé");
        }));
        run("ForeverPilot : package Ding cassé ne modifie aucun addon", () => WithFixture((game, bundle) =>
        {
            var ding = Path.Combine(game, "Interface", "AddOns", "Ding");
            File.Delete(Path.Combine(ding, "Core", "Engine.lua"));
            var payload = Path.Combine(Path.GetDirectoryName(bundle)!, "Ding");
            File.Delete(Path.Combine(payload, "LICENSE"));
            var target = Path.Combine(game, "Interface", "AddOns", ForeverPilotService.AddonName);
            WriteAddon(target, "1.3.0");
            Reject(() => ForeverPilotService.Install(game, bundle, () => false, payload));
            Require(ForeverPilotService.ReadVersion(target) == "1.3.0" && !ForeverPilotService.IsDingComplete(ding), "mutation avant validation");
        }));
        run("ForeverPilot : échec du second addon restaure Ding", () => WithFixture((game, bundle) =>
        {
            var ding = Path.Combine(game, "Interface", "AddOns", "Ding");
            File.Delete(Path.Combine(ding, "Core", "Engine.lua"));
            var target = Path.Combine(game, "Interface", "AddOns", ForeverPilotService.AddonName);
            File.WriteAllText(target, "existing blocking file");
            Reject(() => ForeverPilotService.Install(game, bundle, () => false, Path.Combine(Path.GetDirectoryName(bundle)!, "Ding")));
            Require(!File.Exists(Path.Combine(ding, "Core", "Engine.lua")), "réparation partielle non annulée");
            Require(File.Exists(Path.Combine(ding, "Core", "Ding.lua")), "ancien Ding non restauré");
            Require(File.ReadAllText(target) == "existing blocking file", "obstacle écrasé");
        }));
        run("ForeverPilot : réparer Ding sans rétrograder un ForeverPilot plus récent", () => WithFixture((game, bundle) =>
        {
            var target = Path.Combine(game, "Interface", "AddOns", ForeverPilotService.AddonName);
            WriteAddon(target, "9.0.0");
            Directory.Delete(Path.Combine(game, "Interface", "AddOns", "Ding"), true);
            ForeverPilotService.Install(game, bundle, () => false, Path.Combine(Path.GetDirectoryName(bundle)!, "Ding"));
            Require(ForeverPilotService.ReadVersion(target) == "9.0.0", "addon récent rétrogradé");
            Require(ForeverPilotService.InspectAt(game, bundle, null, false).Ready, "Ding manquant après réparation");
        }));
        run("ForeverPilot : mauvais Ding et chemins sortants refusés", () => WithFixture((game, bundle) =>
        {
            var payload = Path.Combine(Path.GetDirectoryName(bundle)!, "Ding");
            var toc = Path.Combine(payload, "Ding.toc");
            var original = File.ReadAllText(toc);
            File.WriteAllText(toc, original.Replace("16001", "11507"));
            Reject(() => ForeverPilotService.ValidateDing(payload));
            File.WriteAllText(toc, original + "\n..\\external.lua\n");
            Reject(() => ForeverPilotService.ValidateDing(payload));
        }));
        run("ForeverPilot : WoW démarré pendant la préparation laisse les deux addons absents", () => WithFixture((game, bundle) =>
        {
            var ding = Path.Combine(game, "Interface", "AddOns", "Ding");
            Directory.Delete(ding, true);
            int calls = 0;
            Reject(() => ForeverPilotService.Install(game, bundle, () => ++calls > 1, Path.Combine(Path.GetDirectoryName(bundle)!, "Ding")));
            Require(!Directory.Exists(ding) && !Directory.Exists(Path.Combine(game, "Interface", "AddOns", ForeverPilotService.AddonName)), "installation pendant WoW");
        }));
    }

    private static void WriteAddon(string folder, string version)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ForeverPilotService.AddonName + ".toc"), "## Version: " + version + "\nMain.lua\n");
        File.WriteAllText(Path.Combine(folder, "Main.lua"), "-- " + version);
    }
    private static void WithFixture(Action<string, string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "foreverpilot-tests-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "_forever_");
        var bundle = Path.Combine(root, "bundle");
        try
        {
            Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "Wow.exe"), "fixture");
            var ding = Path.Combine(game, "Interface", "AddOns", "Ding");
            WriteDing(ding);
            WriteDing(Path.Combine(root, "Ding"));
            var bridge = Path.Combine(game, "Interface", "AddOns", "ForeverPilot");
            Directory.CreateDirectory(bridge); File.WriteAllText(Path.Combine(bridge, "bridge.lua"), "keep bridge");
            Directory.CreateDirectory(Path.Combine(game, "WTF")); File.WriteAllText(Path.Combine(game, "WTF", "settings.lua"), "keep settings");
            WriteAddon(bundle, "1.4.1"); test(game, bundle);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
    private static void WriteDing(string folder)
    {
        var files = new[] { "Core\\Ding.lua", "Core\\Data.lua", "Core\\Registry.lua", "Core\\Engine.lua", "DB\\DB.lua", "DB\\Quests.lua", "UI\\Arrow.lua" };
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Ding.toc"), "## Interface: 16001\n## Version: v1.8.1\n" + string.Join('\n', files));
        foreach (var file in files.Concat(new[] { "Media\\Textures\\arrow.tga", "LICENSE", "DB\\CREDITS.md" }))
        {
            var full = Path.Combine(folder, file); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, "fixture");
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException) { return; }
        throw new Exception("Une opération invalide a été acceptée.");
    }
}
