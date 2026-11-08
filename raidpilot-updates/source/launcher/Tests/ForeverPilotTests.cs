using RaidPilot.Launcher;

internal static class ForeverPilotTests
{
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
            Directory.CreateDirectory(ding); File.WriteAllText(Path.Combine(ding, "Ding.toc"), "## Title: Ding");
            var bridge = Path.Combine(game, "Interface", "AddOns", "ForeverPilot");
            Directory.CreateDirectory(bridge); File.WriteAllText(Path.Combine(bridge, "bridge.lua"), "keep bridge");
            Directory.CreateDirectory(Path.Combine(game, "WTF")); File.WriteAllText(Path.Combine(game, "WTF", "settings.lua"), "keep settings");
            WriteAddon(bundle, "1.4.1"); test(game, bundle);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException) { return; }
        throw new Exception("Une opération invalide a été acceptée.");
    }
}
