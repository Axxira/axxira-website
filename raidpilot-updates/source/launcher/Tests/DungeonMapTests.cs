namespace RaidPilot.Launcher;
internal static class DungeonMapTests
{
    internal static void Run(Action<string, Action> run, Action<bool, string> require)
    {
        run("Cartes : installation gratuite, sauvegarde et protection des autres addons", () => {
            var root = Path.Combine(Path.GetTempPath(), "DungeonMapTests-" + Guid.NewGuid().ToString("N"));
            var game = Path.Combine(root, "_forever_"); var bundle = Path.Combine(root, "bundle");
            Directory.CreateDirectory(game); Directory.CreateDirectory(bundle); File.WriteAllText(Path.Combine(game, "Wow.exe"), "fixture");
            foreach (var file in DungeonMapService.Files) File.WriteAllText(Path.Combine(bundle, file), "-- test fixture");
            var toc = "## Version: 0.3.0\n" + string.Join("\n", DungeonMapService.Files.Where(f => f.EndsWith(".lua")));
            File.WriteAllText(Path.Combine(bundle, "DungeonMapPilot.toc"), toc);
            var access = Path.Combine(bundle, "Access.lua"); File.WriteAllText(access, "A.distribution = \"raidpilot-test\"");
            var target = Path.Combine(game,"Interface","AddOns","DungeonMapPilot");
            var wtf = Path.Combine(game, "WTF"); Directory.CreateDirectory(wtf); File.WriteAllText(Path.Combine(wtf,"settings"), "keep");
            var bridge = Path.Combine(game,"Interface","AddOns","ForeverPilot"); Directory.CreateDirectory(bridge); File.WriteAllText(Path.Combine(bridge,"private"),"keep");
            void Refused(Action action) { bool blocked=false; try { action(); } catch(InvalidOperationException) {blocked=true;} catch(InvalidDataException){blocked=true;} require(blocked,"opération dangereuse acceptée"); }
            try {
                Refused(()=>DungeonMapService.Install(game,bundle,()=>true));
                require(!Directory.Exists(target),"installation pendant WoW");
                File.WriteAllText(access,"A.distribution = \"public\""); Refused(()=>DungeonMapService.Install(game,bundle,()=>false));
                File.WriteAllText(access,"A.distribution = \"raidpilot-test\"");
                DungeonMapService.Install(game,bundle,()=>false);
                require(File.ReadAllText(Path.Combine(target,"Access.lua")).Contains("raidpilot-test"),"édition gratuite perdue");
                File.WriteAllText(Path.Combine(target,"Core.lua"),"previous");
                var backup=DungeonMapService.Install(game,bundle,()=>false);
                require(File.ReadAllText(Path.Combine(backup,"Core.lua"))=="previous","sauvegarde perdue");
                File.WriteAllText(Path.Combine(target,"DungeonMapPilot.toc"),toc.Replace("0.3.0","9.0.0"));
                Refused(()=>DungeonMapService.Install(game,bundle,()=>false));
                require(File.ReadAllText(Path.Combine(wtf,"settings"))=="keep"&&File.ReadAllText(Path.Combine(bridge,"private"))=="keep","réglages/bridge modifiés");
                File.WriteAllText(Path.Combine(target,"DungeonMapPilot.toc"),toc);
                File.WriteAllText(Path.Combine(target,"personal.lua"),"keep"); Refused(()=>DungeonMapService.Install(game,bundle,()=>false));
            } finally { Directory.Delete(root,true); }
        });
    }
}
