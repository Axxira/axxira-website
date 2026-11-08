using System.IO.Compression;
using System.Security.Cryptography;
using RaidPilot.Launcher;

internal static class ChangeLayerTests
{
    private static readonly string[] Files = ["Bindings.xml", "ChangeLayer.toc", "Core.lua", "Network.lua", "Options.lua", "Plate.lua"];
    private static byte[] Archive(string version = "1.1.4", string? omit = null, string? extra = null, string? tocExtra = null)
    {
        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
        {
            foreach (var name in Files.Where(n => n != omit))
            {
                using var writer = new StreamWriter(zip.CreateEntry("ChangeLayer/" + name).Open());
                writer.Write(name == "ChangeLayer.toc" ? "## Interface: 16001\n## Version: " + version + "\nCore.lua\nNetwork.lua\nOptions.lua\nPlate.lua\n" + tocExtra : "original content");
            }
            if (extra is not null) { using var writer = new StreamWriter(zip.CreateEntry(extra).Open()); writer.Write("unexpected"); }
        }
        return data.ToArray();
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Target(string game) => Path.Combine(game, "Interface", "AddOns", "ChangeLayer");
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException) { return; }
        throw new Exception("Unsafe archive accepted");
    }
    private static void Fixture(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "ChangeLayerTests-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "_forever_");
        Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "Wow.exe"), "fixture");
        try { test(game); } finally { Directory.Delete(root, true); }
    }
    internal static void VerifyOfficial(string path) => Fixture(game =>
    {
        var bytes = File.ReadAllBytes(path);
        ChangeLayerService.InstallArchive(game, bytes, ChangeLayerService.ArchiveHash, () => false);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        foreach (var entry in zip.Entries.Where(e => e.Length > 0))
        {
            using var original = entry.Open();
            using var installed = File.OpenRead(Path.Combine(game, "Interface", "AddOns", entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            Require(SHA256.HashData(original).SequenceEqual(SHA256.HashData(installed)), "Official file modified");
        }
        Require(ChangeLayerService.InstalledVersion(game) == "1.1.4", "Official package not detected");
    });
    internal static void Run(Action<string, Action> run)
    {
        run("ChangeLayer: complete install preserves bridge and settings", () => Fixture(game =>
        {
            var bridge = Path.Combine(game, "Interface", "AddOns", "ForeverPilot"); Directory.CreateDirectory(bridge);
            File.WriteAllText(Path.Combine(bridge, "bridge.lua"), "keep"); Directory.CreateDirectory(Path.Combine(game, "WTF"));
            File.WriteAllText(Path.Combine(game, "WTF", "settings.lua"), "keep");
            var bytes = Archive(); ChangeLayerService.InstallArchive(game, bytes, Hash(bytes), () => false);
            Require(ChangeLayerService.InstalledVersion(game) == "1.1.4", "Version missing");
            Require(File.ReadAllText(Path.Combine(bridge, "bridge.lua")) == "keep" && File.ReadAllText(Path.Combine(game, "WTF", "settings.lua")) == "keep", "Unrelated files modified");
        }));
        run("ChangeLayer: reject bad hash without install", () => Fixture(game =>
        {
            Reject(() => ChangeLayerService.InstallArchive(game, Archive(), "bad", () => false));
            Require(!Directory.Exists(Target(game)), "Files installed on hash failure");
        }));
        run("ChangeLayer: reject incomplete, unexpected, duplicate, wrong-version archives", () =>
        {
            foreach (var bytes in new[] { Archive(omit: "Core.lua"), Archive(extra: "ChangeLayer/../ForeverPilot/bridge.lua"), Archive(extra: "ChangeLayer/tool.exe"), Archive(extra: "ChangeLayer/Core.lua"), Archive("1.1.5"), Archive(tocExtra: "../other.lua") })
                Fixture(game => { Reject(() => ChangeLayerService.InstallArchive(game, bytes, Hash(bytes), () => false)); Require(!Directory.Exists(Target(game)), "Invalid package installed"); });
        });
        run("ChangeLayer: refuse running or newly opened game", () => Fixture(game =>
        {
            var bytes = Archive(); var calls = 0;
            Reject(() => ChangeLayerService.InstallArchive(game, bytes, Hash(bytes), () => true));
            Reject(() => ChangeLayerService.InstallArchive(game, bytes, Hash(bytes), () => ++calls > 1));
            Require(!Directory.Exists(Target(game)), "Installed while game running");
        }));
        run("ChangeLayer: back up old version and repair partial install", () => Fixture(game =>
        {
            var bytes = Archive(); ChangeLayerService.InstallArchive(game, bytes, Hash(bytes), () => false);
            File.WriteAllText(Path.Combine(Target(game), "ChangeLayer.toc"), "## Interface: 16001\n## Version: 1.1.3\nCore.lua\n");
            ChangeLayerService.InstallArchive(game, bytes, Hash(bytes), () => false);
            var backups = Directory.GetDirectories(Path.Combine(game, "Interface", "ForeverPilotBackups"), "ChangeLayer-*");
            Require(backups.Length == 1 && File.ReadAllText(Path.Combine(backups[0], "ChangeLayer.toc")).Contains("1.1.3"), "Backup missing");
            File.Delete(Path.Combine(Target(game), "Core.lua")); Require(ChangeLayerService.InstalledVersion(game) is null, "Partial install accepted");
            ChangeLayerService.InstallArchive(game, bytes, Hash(bytes), () => false);
            Require(ChangeLayerService.InstalledVersion(game) == "1.1.4", "Partial repair failed");
        }));
        run("ChangeLayer: preserve newer version even when incomplete", () => Fixture(game =>
        {
            var bytes = Archive(); ChangeLayerService.InstallArchive(game, bytes, Hash(bytes), () => false);
            var toc = Path.Combine(Target(game), "ChangeLayer.toc");
            File.WriteAllText(toc, "## Interface: 16001\n## Version: 1.2.0\nCore.lua\n");
            ChangeLayerService.InstallArchive(game, bytes, Hash(bytes), () => false);
            Require(ChangeLayerService.InstalledVersion(game) == "1.2.0", "New version downgraded");
            File.Delete(Path.Combine(Target(game), "Core.lua"));
            Reject(() => ChangeLayerService.InstallArchive(game, bytes, Hash(bytes), () => false));
            Require(File.ReadAllText(toc).Contains("1.2.0"), "Partial new version overwritten");
        }));
    }
}
