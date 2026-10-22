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
    Require(UpdateService.NormalizeVersionForDisplay("0.14.2.9") == "0.14.2.9",
        "numéro du hotfix masqué dans le launcher");
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
Run("self-update travaille hors du dossier launcher", () =>
{
    var workDirectory = UpdateService.GetLauncherUpdaterWorkingDirectory();
    Require(Path.IsPathRooted(workDirectory), "dossier updater non absolu");
    Require(workDirectory.Contains(
            Path.Combine("RaidPilot Launcher", "Updater"),
            StringComparison.OrdinalIgnoreCase),
        "dossier updater inattendu");

    var script = UpdateService.BuildLauncherSelfUpdateScript();
    Require(script.Contains("[Parameter(Mandatory=$true)][string]$UpdaterWork", StringComparison.Ordinal),
        "dossier de travail sécurisé non transmis au script");
    Require(script.Contains("Set-Location -LiteralPath $UpdaterWork", StringComparison.Ordinal),
        "script reste dans le dossier du launcher");
    Require(script.Contains("FALLBACK_RELAUNCH", StringComparison.Ordinal),
        "relance de secours absente");
    Require(script.Contains("self-update.log", StringComparison.Ordinal),
        "journal self-update absent");
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
    Require(script.Contains("Start-Process -FilePath $fallbackExe", StringComparison.Ordinal),
        "relance du launcher restauré/de secours absente");
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
        var profilePath = Path.Combine("UserData", "EBWebView", "Default", "Local Storage", "settings.test");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(target, profilePath))!);
        await File.WriteAllTextAsync(Path.Combine(target, profilePath), "filters-and-calibration");
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
        Require(await File.ReadAllTextAsync(Path.Combine(target, profilePath)) == "filters-and-calibration",
            "réglages de carte perdus pendant la mise à jour");
        Require(await File.ReadAllTextAsync(Path.Combine(target + ".previous", profilePath)) == "filters-and-calibration",
            "réglages absents du rollback");
        Require(UpdateService.Rollback(target, "Dummy.exe"), "rollback refusé");
        Require(await File.ReadAllTextAsync(Path.Combine(target, "Dummy.exe")) == "v1",
            "ancienne version non restaurée");
    }
    finally { SafeDelete(root); }
});
Run("payload embarqué conserve les réglages WebView2", () =>
{
    var root = CreateTemp();
    try
    {
        var target = Path.Combine(root, "app");
        var bundled = Path.Combine(root, "bundle");
        var relative = Path.Combine("UserData", "EBWebView", "Default", "Local Storage", "settings.test");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(target, relative))!);
        File.WriteAllText(Path.Combine(target, relative), "personal-calibration");
        Directory.CreateDirectory(bundled);
        File.Copy(typeof(UpdateService).Assembly.Location, Path.Combine(bundled, "Dummy.exe"));
        UpdateService.EnsureBundledComponentInstalled(bundled, target, "Dummy.exe");
        Require(File.Exists(Path.Combine(target, "Dummy.exe")), "payload non installé");
        Require(File.ReadAllText(Path.Combine(target, relative)) == "personal-calibration",
            "profil perdu à l'installation du payload");
        Require(File.ReadAllText(Path.Combine(target + ".previous", relative)) == "personal-calibration",
            "profil perdu dans la sauvegarde");
        Require(!Directory.Exists(Path.Combine(bundled, "UserData")), "profil personnel copié dans le paquet public");
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

Run("migration depuis ancien launcher sans écraser le profil actuel", () =>
{
    var root = CreateTemp();
    try
    {
        var target = Path.Combine(root, "installed");
        var previous = target + ".previous";
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.Combine(previous, "UserData", "Default"));
        var saved = Path.Combine("UserData", "Default", "settings");
        File.WriteAllText(Path.Combine(previous, saved), "filtres-et-calage");
        UpdateService.EnsureBundledComponentInstalled(Path.Combine(root, "missing-bundle"), target, "RaidPilot.Overlay.exe");
        Require(File.ReadAllText(Path.Combine(target, saved)) == "filtres-et-calage", "profil précédent non restauré avant retour anticipé");
        Require(File.Exists(Path.Combine(previous, saved)), "sauvegarde supprimée");
        File.WriteAllText(Path.Combine(target, saved), "reglage-recent");
        UpdateService.RecoverPreviousUserData(target);
        Require(File.ReadAllText(Path.Combine(target, saved)) == "reglage-recent", "profil courant écrasé");
        Require(UpdateService.IsNewer("0.14.2.9", "0.14.2.10"), "HOTFIX10 non proposé après HOTFIX9");
        Require(UpdateService.ShouldInstallBundledVersion("0.14.2.9", "0.14.2.10+build"), "payload signé par une révision Git ignoré");
    }
    finally { SafeDelete(root); }
});

if (failures.Count == 0)
{
    Console.WriteLine("17/17 tests launcher réussis.");
    return 0;
}
Console.Error.WriteLine($"{failures.Count}/17 test(s) launcher en échec");
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
