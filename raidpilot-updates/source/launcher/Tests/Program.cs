using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RaidPilot.Launcher;
using RaidPilot.Overlay;

var failures = new List<string>();
Run("comparaison de versions", () =>
{
    Require(UpdateService.IsNewer("0.10.2", "0.11.0"), "0.11.0 devrait être plus récent");
    Require(!UpdateService.IsNewer("0.11.0", "0.10.2"), "downgrade considéré comme update");
    Require(!UpdateService.IsNewer("0.11.0", "0.11.0"), "version égale considérée nouvelle");
    Require(!UpdateService.IsNewer("0.11.4+65c0b97cd781", "0.11.4"),
        "suffixe Git Overlay considéré comme une mise à jour");
    Require(!UpdateService.IsNewer("0.2.1+65c0b97cd781", "0.2.1"),
        "suffixe Git Beacon considéré comme une mise à jour");
    Require(UpdateService.IsNewer("0.11.4+65c0b97cd781", "0.11.5"),
        "vraie mise à niveau masquée par le suffixe Git");
});
Run("source HTTP refusée", () =>
{
    Require(!UpdateService.IsAllowedManifestLocation("http://example.com/manifest.json"),
        "HTTP non chiffré accepté");
    Require(UpdateService.IsAllowedManifestLocation("https://example.com/manifest.json"),
        "HTTPS refusé");
});
Run("payload embarqué met à niveau seulement vers une version plus récente", () =>
{
    Require(UpdateService.ShouldInstallBundledVersion(null, "0.11.1"),
        "installation initiale refusée");
    Require(UpdateService.ShouldInstallBundledVersion("0.11.0", "0.11.1"),
        "upgrade embarqué refusé");
    Require(!UpdateService.ShouldInstallBundledVersion("0.11.1", "0.11.1"),
        "version identique réinstallée");
    Require(!UpdateService.ShouldInstallBundledVersion("0.12.0", "0.11.1"),
        "downgrade embarqué accepté");
});
Run("canal Stable officiel intégré", () =>
{
    Require(UpdateService.IsAllowedManifestLocation(LauncherSettings.StableManifestUrl),
        "URL Stable intégrée invalide");
    Require(LauncherSettings.StableManifestUrl.Contains("raidpilot-updates", StringComparison.Ordinal),
        "URL Stable ne vise pas le canal RaidPilot");
});
Run("état à jour ignore les suffixes Git", () =>
{
    var manifest = new UpdateManifest
    {
        SchemaVersion = 1,
        Channel = "stable",
        RaidPilot = new UpdateComponent
        {
            Version = "0.11.4",
            Url = "https://example.com/overlay.zip",
            Sha256 = new string('A', 64),
        },
        Beacon = new UpdateComponent
        {
            Version = "0.2.1",
            Url = "https://example.com/beacon.zip",
            Sha256 = new string('B', 64),
        },
        Launcher = new UpdateComponent
        {
            Version = "0.2.7",
            Url = "https://example.com/launcher.zip",
            Sha256 = new string('C', 64),
        },
    };
    Require(!UpdateService.HasAvailableUpdate(
            "0.11.4+65c0b97cd781",
            "0.2.1+65c0b97cd781",
            "0.2.7",
            manifest),
        "bouton de mise à jour resterait actif alors que tout est à jour");
});
Run("version affichée suit le binaire réel", () =>
{
    Require(UpdateService.NormalizeVersionForDisplay("0.2.7") == "0.2.7",
        "version simple mal affichée");
    Require(UpdateService.NormalizeVersionForDisplay("0.2.3+63309c9f") == "0.2.3",
        "suffixe Git visible dans l'interface");
});
Run("détection automatique inclut le launcher", () =>
{
    var manifest = new UpdateManifest
    {
        SchemaVersion = 1,
        Channel = "stable",
        Launcher = new UpdateComponent
        {
            Version = "0.2.5",
            Url = "https://example.com/launcher.zip",
            Sha256 = new string('A', 64),
        },
    };
    Require(UpdateService.HasAvailableUpdate("0.11.4", "0.2.1", "0.2.4", manifest),
        "une mise à jour launcher seule n'est pas détectée");
    Require(!UpdateService.HasAvailableUpdate("0.11.4", "0.2.1", "0.2.5", manifest),
        "version launcher identique considérée nouvelle");
    Require(UpdateService.AutomaticUpdateCheckIntervalMilliseconds >= 60_000
        && UpdateService.AutomaticUpdateCheckIntervalMilliseconds <= 600_000,
        "intervalle de vérification automatique déraisonnable");
});
Run("self-update accepte les suffixes de build Git", () =>
{
    var script = UpdateService.BuildLauncherSelfUpdateScript();
    Require(script.Contains("($actualVersion -split '[+-]', 2)[0]", StringComparison.Ordinal),
        "suffixe +commit du ProductVersion non normalisé");
    Require(script.Contains("[Version]::TryParse($actualCore", StringComparison.Ordinal),
        "comparaison sémantique de version absente");
});
Run("self-update relance et restaure en cas d'échec", () =>
{
    var script = UpdateService.BuildLauncherSelfUpdateScript();
    Require(script.Contains(
            "Start-Process -FilePath $newExe -WorkingDirectory $Target -PassThru -ErrorAction Stop",
            StringComparison.Ordinal),
        "relance du nouveau launcher absente");
    Require(script.Contains("$relaunch.HasExited", StringComparison.Ordinal),
        "vérification du redémarrage absente");
    Require(script.Contains("Move-Item -LiteralPath $previous -Destination $Target", StringComparison.Ordinal),
        "restauration de l'ancienne version absente");
    Require(script.Contains("Start-Process -FilePath $rollbackExe", StringComparison.Ordinal),
        "relance du launcher restauré absente");
});
await RunAsync("manifeste local validé", async () =>
{
    var root = CreateTemp();
    try
    {
        var package = CreatePackage(root, "Dummy.exe", "v2");
        var component = BuildComponent(package, "2.0.0");
        var manifestPath = Path.Combine(root, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            channel = "stable",
            raidPilot = component,
        }));
        using var service = new UpdateService();
        var manifest = await service.LoadManifestAsync(new Uri(manifestPath).AbsoluteUri, CancellationToken.None);
        Require(manifest.RaidPilot?.Version == "2.0.0", "version manifeste perdue");
    }
    finally { SafeDelete(root); }
});
await RunAsync("installation atomique et rollback", async () =>
{
    var root = CreateTemp();
    try
    {
        var target = Path.Combine(root, "app");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "Dummy.exe"), "v1");
        var package = CreatePackage(root, "Dummy.exe", "v2");
        var component = BuildComponent(package, "2.0.0");
        using var service = new UpdateService();
        await service.InstallComponentAsync(
            component,
            target,
            "Dummy.exe",
            null,
            CancellationToken.None);
        Require(await File.ReadAllTextAsync(Path.Combine(target, "Dummy.exe")) == "v2",
            "nouvelle version non installée");
        Require(Directory.Exists(target + ".previous"), "rollback non conservé");
        Require(UpdateService.Rollback(target, "Dummy.exe"), "rollback refusé");
        Require(await File.ReadAllTextAsync(Path.Combine(target, "Dummy.exe")) == "v1",
            "ancienne version non restaurée");
    }
    finally { SafeDelete(root); }
});
await RunAsync("SHA invalide préserve la version courante", async () =>
{
    var root = CreateTemp();
    try
    {
        var target = Path.Combine(root, "app");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "Dummy.exe"), "v1");
        var package = CreatePackage(root, "Dummy.exe", "v2");
        var component = BuildComponent(package, "2.0.0") with
        {
            Sha256 = new string('0', 64),
        };
        using var service = new UpdateService();
        var failed = false;
        try
        {
            await service.InstallComponentAsync(
                component,
                target,
                "Dummy.exe",
                null,
                CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }
        Require(failed, "SHA invalide accepté");
        Require(await File.ReadAllTextAsync(Path.Combine(target, "Dummy.exe")) == "v1",
            "version courante endommagée après SHA invalide");
    }
    finally { SafeDelete(root); }
});

Run("code d'escouade launcher valide", () =>
{
    var code = SquadRoomCode.Generate();
    Require(SquadRoomCode.TryNormalize(code, out var normalized), "code généré illisible");
    Require(normalized.Length == 16, "code compact invalide");
    Require(SquadRoomCode.Format(normalized) == code, "format du code non stable");
});
Run("profil d'escouade launcher persistant", () =>
{
    var root = CreateTemp();
    try
    {
        var path = Path.Combine(root, "squad-profile.json");
        var profile = new SquadProfile(
            "launcher-test-" + Guid.NewGuid().ToString("N"),
            "Pierre",
            SquadRoomCode.Generate(),
            true);
        Require(SquadProfileStore.TrySave(profile, path), "sauvegarde profil refusée");
        var reloaded = SquadProfileStore.LoadOrCreate(path);
        Require(reloaded.MemberId == profile.MemberId, "identité du profil perdue");
        Require(reloaded.Name == "Pierre", "nom du profil perdu");
        Require(reloaded.RoomCode == profile.RoomCode, "code d'escouade perdu");
        Require(reloaded.AutoJoin, "auto-join perdu");
    }
    finally
    {
        SafeDelete(root);
    }
});

if (failures.Count == 0)
{
    Console.WriteLine("14/14 tests launcher réussis.");
    return 0;
}
Console.Error.WriteLine($"{failures.Count}/14 test(s) launcher en échec");
foreach (var failure in failures) Console.Error.WriteLine("- " + failure);
return 1;

void Run(string name, Action test)
{
    try { test(); Console.WriteLine("OK  " + name); }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}
async Task RunAsync(string name, Func<Task> test)
{
    try { await test(); Console.WriteLine("OK  " + name); }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static string CreateTemp()
{
    var path = Path.Combine(Path.GetTempPath(), "raidpilot-launcher-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}
static string CreatePackage(string root, string exeName, string content)
{
    var source = Path.Combine(root, "source-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, exeName), content);
    var zip = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
    ZipFile.CreateFromDirectory(source, zip);
    Directory.Delete(source, true);
    return zip;
}
static UpdateComponent BuildComponent(string package, string version)
{
    using var sha = SHA256.Create();
    using var stream = File.OpenRead(package);
    var hash = Convert.ToHexString(sha.ComputeHash(stream));
    return new UpdateComponent
    {
        Version = version,
        Url = new Uri(package).AbsoluteUri,
        Sha256 = hash,
        SizeBytes = new FileInfo(package).Length,
        ReleaseNotes = "Test",
    };
}
static void SafeDelete(string path)
{
    try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
}