using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
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
Run("source désactivée reste désactivée au démarrage", () =>
{
    var root = CreateTemp();
    try
    {
        var configPath = Path.Combine(root, "disabled-feed.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(
            new LauncherSettings("", "stable", AutoInstallWhenIdle: false)));
        var loaded = LauncherSettingsStore.LoadFromPath(configPath);
        Require(loaded.ManifestUrl == "", "la source désactivée a été remplacée par Stable");
    }
    finally { SafeDelete(root); }
});
Run("mise en page du launcher adaptée aux petits écrans", () =>
{
    var small = LauncherCanvasLayout.FitWindow(new System.Drawing.Size(900, 600));
    Require(small.Width < 1180 && small.Height < 760,
        "la taille initiale du launcher dépasse le moniteur");
    var transform = LauncherCanvasLayout.Fit(small);
    var screenCenter = new System.Drawing.Point(
        (int)Math.Round(transform.OffsetX + transform.Scale * 590),
        (int)Math.Round(transform.OffsetY + transform.Scale * 380));
    var virtualCenter = transform.ToCanvas(screenCenter);
    Require(Math.Abs(virtualCenter.X - 590) <= 2
            && Math.Abs(virtualCenter.Y - 380) <= 2,
        "les clics au centre ne suivent pas l'échelle de l'interface");
});
Run("source HTTP refusée", () =>
{
    Require(!UpdateService.IsAllowedManifestLocation("http://example.com/manifest.json"),
        "HTTP non chiffré accepté");
    Require(UpdateService.IsAllowedManifestLocation("https://example.com/manifest.json"),
        "HTTPS refusé");
});
Run("rafraîchissement manifeste GitHub sans cache", () =>
{
    var official = new Uri(LauncherSettings.StableManifestUrl);
    var refreshed = UpdateService.BuildFreshManifestUri(official, 1760000000000L);
    Require(refreshed.Scheme == Uri.UriSchemeHttps, "HTTPS perdu lors du rafraîchissement");
    Require(refreshed.AbsoluteUri.Contains("raidpilot_refresh=1760000000000", StringComparison.Ordinal),
        "paramètre anti-cache absent du canal officiel");
    Require(refreshed.AbsoluteUri.StartsWith(official.GetLeftPart(UriPartial.Path), StringComparison.Ordinal),
        "adresse du manifeste officiel modifiée");
    var custom = new Uri("https://example.com/stable.json?tenant=x");
    Require(UpdateService.BuildFreshManifestUri(custom, 123L) == custom,
        "source HTTPS personnalisée altérée");
    var local = new Uri("file:///C:/RaidPilot/manifest.json");
    Require(UpdateService.BuildFreshManifestUri(local, 123L) == local,
        "source locale altérée");
    var withQuery = new Uri(official.AbsoluteUri + "?channel=stable");
    Require(UpdateService.BuildFreshManifestUri(withQuery, 42L).Query.Contains("channel=stable&raidpilot_refresh=42", StringComparison.Ordinal),
        "paramètres existants non préservés");
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
Run("Stable: trois archives officielles de la même publication", () =>
{
    const string tag = "raidpilot-stable-rp0.14.2.24-b0.3.9-l0.2.15-abcdefabcdef";
    const string root = "https://github.com/Axxira/axxira-website/releases/download/";
    var m = new UpdateManifest
    {
        SchemaVersion = 1,
        RaidPilot = new UpdateComponent
        {
            Version = "0.14.2.24", Url = root + tag
                + "/RaidPilot-Overlay-v0.14.2.24-Windows-x64.zip",
            Sha256 = new string('A', 64), SizeBytes = 50000,
        },
        Beacon = new UpdateComponent
        {
            Version = "0.3.9", Url = root + tag
                + "/RaidPilot-Beacon-v0.3.9-Windows-x64.zip",
            Sha256 = new string('B', 64), SizeBytes = 50000,
        },
        Launcher = new UpdateComponent
        {
            Version = "0.2.15", Url = root + tag
                + "/RaidPilot-Launcher-v0.2.15-Windows-x64.zip",
            Sha256 = new string('C', 64), SizeBytes = 50000,
        },
    };
    UpdateService.ValidateOfficialStableManifestSources(
        LauncherSettings.StableManifestUrl, m);
});
Run("Stable: une archive extérieure ou version mixte est rejetée", () =>
{
    var m = new UpdateManifest { SchemaVersion = 1, RaidPilot = new UpdateComponent
    {
        Version = "0.14.2.24",
        Url = "https://untrusted.example.org/malicious-overlay.zip",
        Sha256 = new string('A', 64), SizeBytes = 50,
    }};
    var denied = false;
    try { UpdateService.ValidateOfficialStableManifestSources(
        LauncherSettings.StableManifestUrl, m); }
    catch (InvalidOperationException) { denied = true; }
    Require(denied, "Une source extérieure pourrait remplacer la version Stable.");
    // Custom local/private feed is an explicit user-controlled opt-in.
    UpdateService.ValidateOfficialStableManifestSources("file:///C:/custom.json", m);
});
Run("Stable: versions provenant de deux publications différentes refusées", () =>
{
    const string root = "https://github.com/Axxira/axxira-website/releases/download/";
    var manifest = new UpdateManifest
    {
        SchemaVersion = 1,
        RaidPilot = new UpdateComponent
        {
            Version = "0.14.2.24",
            Url = root + "raidpilot-stable-rp0.14.2.24-b0.3.9-l0.2.15-aabbccddeeff/"
                + "RaidPilot-Overlay-v0.14.2.24-Windows-x64.zip",
            Sha256 = new string('A', 64), SizeBytes = 25,
        },
        Beacon = new UpdateComponent
        {
            Version = "0.3.9",
            Url = root + "raidpilot-stable-rp0.14.2.23-b0.3.9-l0.2.14-112233445566/"
                + "RaidPilot-Beacon-v0.3.9-Windows-x64.zip",
            Sha256 = new string('B', 64), SizeBytes = 25,
        },
    };
    var failed = false;
    try { UpdateService.ValidateOfficialStableManifestSources(
        LauncherSettings.StableManifestUrl, manifest); }
    catch (InvalidOperationException) { failed = true; }
    Require(failed, "Le manifeste mélange deux publications officielles.");
});
Run("Launcher self-update: empreinte revalidée après fermeture", () =>
{
    var script = UpdateService.BuildLauncherSelfUpdateScript();
    Require(script.Contains("Get-FileHash -LiteralPath $Archive -Algorithm SHA256",
        StringComparison.Ordinal) && script.Contains("$ExpectedSha256",
        StringComparison.Ordinal), "Le script de remplacement ne vérifie pas la SHA après la fermeture.");
});
Run("Package: le lanceur détecte les éléments absents au démarrage", () =>
{
    var root = CreateTemp();
    try
    {
        var ok = LauncherPackageVerifier.Verify(root, out var problem);
        Require(!ok && problem.Contains("RaidPilot.Launcher.exe", StringComparison.Ordinal),
            "Un lanceur sans binaire ne doit pas passer le smoke test.");
    }
    finally { SafeDelete(root); }
});
Run("Payload: un exécutable sans version publiée est refusé", () =>
{
    var root = CreateTemp();
    try
    {
        var exe = Path.Combine(root, "RaidPilot.Overlay.exe");
        File.WriteAllText(exe, "not a real release");
        var denied = false;
        try { UpdateService.VerifyNativePayloadVersion(
            exe, "RaidPilot.Overlay.exe", "0.14.2.24"); }
        catch (InvalidOperationException) { denied = true; }
        Require(denied, "Un faux exécutable Overlay n'a pas été rejeté.");
        UpdateService.VerifyNativePayloadVersion(exe, "Dummy.exe", "test");
    }
    finally { SafeDelete(root); }
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
Run("repli transactionnel des composants vérifié", () =>
{
    var root = CreateTemp();
    try
    {
        var target = Path.Combine(root, "component");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "Dummy.exe"), "v2");
        var old = target + ".previous";
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "Dummy.exe"), "v1");
        Require(UpdateService.RollbackOrRemove(target, "Dummy.exe", true)
                && File.ReadAllText(Path.Combine(target, "Dummy.exe")) == "v1",
            "composant précédent non restauré");
        Require(UpdateService.RollbackOrRemove(target, "Dummy.exe", false)
                && !Directory.Exists(target),
            "nouveau composant sans ancienne version non supprimé");
    }
    finally { SafeDelete(root); }
});
Run("reprise après interruption de remplacement", () =>
{
    var root = CreateTemp();
    try
    {
        var live = Path.Combine(root, "live");
        Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, "incomplete.txt"), "staged");
        Directory.CreateDirectory(live + ".previous");
        File.WriteAllText(Path.Combine(live + ".previous", "Dummy.exe"), "healthy-v1");
        Require(UpdateService.RecoverInterruptedComponent(live, "Dummy.exe"),
            "ancien exécutable non récupéré après coupure");
        Require(File.ReadAllText(Path.Combine(live, "Dummy.exe")) == "healthy-v1",
            "retour arrière incomplet");
        Require(!UpdateService.RecoverInterruptedComponent(live, "Dummy.exe"),
            "version restaurée remplacée à tort");
    }
    finally { SafeDelete(root); }
});
Run("archive refuse le zip-slip et les chemins dupliqués", () =>
{
    var root = CreateTemp();
    try
    {
        var path = Path.Combine(root, "danger.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var content = new StreamWriter(archive.CreateEntry("../outside.exe").Open());
            content.Write("not executable");
        }
        var refused = false;
        try { UpdateService.ValidateZipArchive(path); }
        catch (InvalidOperationException) { refused = true; }
        Require(refused, "zip-slip accepté");
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
await RunAsync("HTTP: interruption suivie d'une reprise Range valide", async () =>
{
    var root = CreateTemp();
    try
    {
        var zip = CreatePackage(root, "Dummy.exe", "v2-with-ranged-network-resume");
        var handler = new SimulatedPartialDownloadHandler(File.ReadAllBytes(zip), ignoreRange: false);
        using var http = new HttpClient(handler);
        using var updater = new UpdateService(http);
        var installed = Path.Combine(root, "installed");
        var component = BuildComponent(zip, "2.0.0") with { Url = "https://download.example.org/package.zip" };
        await updater.InstallComponentAsync(component, installed, "Dummy.exe", null,
            CancellationToken.None);
        Require(handler.RequestCount == 2 && handler.ObservedRange,
            "La reprise HTTP n'a pas demandé la suite de l'archive.");
        Require(File.ReadAllText(Path.Combine(installed, "Dummy.exe"))
            == "v2-with-ranged-network-resume", "L'archive reprise est corrompue.");
    }
    finally { SafeDelete(root); }
});
await RunAsync("HTTP: serveur ignorait Range, fichier reconstruit sans duplication", async () =>
{
    var root = CreateTemp();
    try
    {
        var zip = CreatePackage(root, "Dummy.exe", "version-2-full-response-after-range");
        var handler = new SimulatedPartialDownloadHandler(File.ReadAllBytes(zip), ignoreRange: true);
        using var http = new HttpClient(handler);
        using var updater = new UpdateService(http);
        var installed = Path.Combine(root, "installed");
        var component = BuildComponent(zip, "2.0.0") with { Url = "https://download.example.org/ignored-range.zip" };
        await updater.InstallComponentAsync(component, installed, "Dummy.exe", null,
            CancellationToken.None);
        Require(handler.RequestCount == 2 && handler.ObservedRange,
            "La reprise HTTP ne s'est pas présentée.");
        Require(File.ReadAllText(Path.Combine(installed, "Dummy.exe"))
            == "version-2-full-response-after-range", "La réponse HTTP complète n'a pas remplacé l'archive partielle.");
    }
    finally { SafeDelete(root); }
});
Run("Journal: crash entre mise à jour Overlay et Beacon restaure les deux", () =>
{
    var root = CreateTemp();
    try
    {
        var raid = Path.Combine(root, "raid");
        var beacon = Path.Combine(root, "beacon");
        Directory.CreateDirectory(raid); Directory.CreateDirectory(beacon);
        File.WriteAllText(Path.Combine(raid, "RaidPilot.Overlay.exe"), "old-raid");
        File.WriteAllText(Path.Combine(beacon, "RaidPilot.Beacon.exe"), "old-beacon");
        var journal = Path.Combine(root, "journal.json");
        var snapshot = LauncherUpdateRecovery.Capture(true, true,
            Path.Combine(raid, "RaidPilot.Overlay.exe"),
            Path.Combine(beacon, "RaidPilot.Beacon.exe"));
        LauncherUpdateRecovery.Begin(journal, snapshot);
        Directory.Move(raid, raid + ".previous");
        Directory.CreateDirectory(raid);
        File.WriteAllText(Path.Combine(raid, "RaidPilot.Overlay.exe"), "new-raid");
        Require(LauncherUpdateRecovery.Recover(journal, raid, beacon, out _),
            "La récupération journalisée a échoué.");
        Require(File.ReadAllText(Path.Combine(raid, "RaidPilot.Overlay.exe")) == "old-raid"
            && File.ReadAllText(Path.Combine(beacon, "RaidPilot.Beacon.exe")) == "old-beacon"
            && !File.Exists(journal), "Composants mélangés ou journal persistant.");
    }
    finally { SafeDelete(root); }
});
Run("Journal: sauvegarde périmée jamais utilisée si binaire intact", () =>
{
    var root = CreateTemp();
    try
    {
        var raid = Path.Combine(root, "raid");
        Directory.CreateDirectory(raid);
        Directory.CreateDirectory(raid + ".previous");
        File.WriteAllText(Path.Combine(raid, "RaidPilot.Overlay.exe"), "current");
        File.WriteAllText(Path.Combine(raid + ".previous", "RaidPilot.Overlay.exe"), "stale-backup");
        var journal = Path.Combine(root, "journal.json");
        LauncherUpdateRecovery.Begin(journal,
            LauncherUpdateRecovery.Capture(true, false,
                Path.Combine(raid, "RaidPilot.Overlay.exe"), Path.Combine(root, "missing")));
        Require(LauncherUpdateRecovery.Recover(journal, raid, Path.Combine(root, "beacon"), out _),
            "Récupération devrait reconnaître l'installation inchangée.");
        Require(File.ReadAllText(Path.Combine(raid, "RaidPilot.Overlay.exe")) == "current",
            "La sauvegarde périmée a remplacé le binaire intact.");
    }
    finally { SafeDelete(root); }
});
Run("Journal: installation interrompue d'un composant neuf est supprimée", () =>
{
    var root = CreateTemp();
    try
    {
        var raid = Path.Combine(root, "raid");
        var journal = Path.Combine(root, "journal.json");
        LauncherUpdateRecovery.Begin(journal,
            LauncherUpdateRecovery.Capture(true, false,
                Path.Combine(raid, "RaidPilot.Overlay.exe"), Path.Combine(root, "missing")));
        Directory.CreateDirectory(raid);
        File.WriteAllText(Path.Combine(raid, "RaidPilot.Overlay.exe"), "incomplete-new");
        Require(LauncherUpdateRecovery.Recover(journal, raid, Path.Combine(root, "beacon"), out _)
            && !Directory.Exists(raid), "Le composant neuf partiellement installé subsiste.");
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


Run("notes: historique absent accepté et manifeste ancien compatible", () =>
{
    var legacy = new UpdateManifest {
        SchemaVersion = 1,
        RaidPilot = new UpdateComponent { Version = "0.14.2.18", ReleaseNotes = "Zoom sauvegardé." }
    };
    Require(ReleaseHistoryCatalog.FromManifest(legacy).Count == 0,
        "ancien manifeste ne devrait pas produire de faux historiques");
    Require(ReleaseHistoryCatalog.FormatDetailed(legacy).Contains("Zoom sauvegardé."),
        "anciennes notes de composants perdues");
});

Run("notes: dernier historique affiché et texte complet disponible", () =>
{
    var latest = SampleReleaseNote("0.2.13", "launcher", "2026-10-23", "Historique dynamique");
    var previous = SampleReleaseNote("0.14.2.18", "raidpilot", "2026-10-23", "Zoom préservé");
    var manifest = new UpdateManifest {
        SchemaVersion = 1,
        ReleaseHistory = [latest, previous]
    };
    var rows = ReleaseHistoryCatalog.FromManifest(manifest);
    Require(rows.Count == 2 && rows[0].Version == "0.2.13"
        && rows[1].Version == "0.14.2.18",
        "les deux dernières mises à jour ne sont pas dans l'ordre");
    var notes = ReleaseHistoryCatalog.FormatDetailed(manifest);
    Require(notes.Contains("v0.2.13") && notes.Contains("v0.14.2.18")
        && notes.Contains("Historique dynamique") && notes.Contains("Zoom préservé"),
        "le bouton Voir les notes n'affiche pas l'historique détaillé");
});

Run("notes: version, date et champ résumé invalides rejetés", () =>
{
    var note = SampleReleaseNote("0.2.13", "launcher", "2026-10-23", "Historique");
    var rows = ReleaseHistoryCatalog.Normalize([
        note with { Version = "n'importe quoi" },
        note with { Date = "2026-99-99" },
        note with { Component = "inconnu" },
        note with { Summary = "Incorrect\nligne" },
        note with { Details = "Corrompu\u0001" },
        note,
        note
    ]);
    Require(rows.Count == 1 && rows[0].Version == "0.2.13",
        "historique corrompu ou version répétée conservé");
});

Run("notes: manifeste JSON transporte le nouvel historique", () =>
{
    var json = JsonSerializer.Serialize(new {
        schemaVersion = 1,
        raidPilot = new { version = "0.14.2.18", url = "https://example.com/overlay.zip", sha256 = new string('a',64) },
        releaseHistory = new [] { SampleReleaseNote("0.2.13", "launcher", "2026-10-23", "Nouveautés") }
    });
    // The generated authoring file uses the same property names as the feed.
    using var document = JsonDocument.Parse(json);
    Require(document.RootElement.TryGetProperty("releaseHistory", out _),
        "historique manquant dans le manifeste");
    var fixture = JsonSerializer.Serialize(new {
        schemaVersion = 1,
        releaseHistory = new [] { new {
            component = "launcher",
            version = "0.2.13",
            raidPilotVersion = "0.14.2.18",
            beaconVersion = "0.3.6",
            launcherVersion = "0.2.13",
            date = "2026-10-23",
            summary = "Notes",
            details = "Évolutions"
        } }
    });
    var parsed = JsonSerializer.Deserialize<UpdateManifest>(fixture)!;
    Require(parsed.ReleaseHistory?.Count == 1
        && ReleaseHistoryCatalog.FromManifest(parsed).Count == 1,
        "le champ JSON du manifeste n'est pas lu");
});

Run("notes: fichier local disponible hors connexion", () =>
{
    var root = CreateTemp();
    try {
        var path = Path.Combine(root, "release-history.json");
        var file = new {
            schemaVersion = 1,
            entries = new [] { new {
                component = "launcher", version = "0.2.13",
                raidPilotVersion = "0.14.2.18",
                beaconVersion = "0.3.6", launcherVersion = "0.2.13",
                date = "2026-10-23", summary = "Notes hors ligne",
                details = "Contenu disponible sans Internet"
            } }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(file));
        var rows = ReleaseHistoryCatalog.ReadBundled(path);
        Require(rows.Count == 1 && rows[0].Summary == "Notes hors ligne",
            "le catalogue de secours local n'est pas chargé");
        File.WriteAllText(path, "broken");
        Require(ReleaseHistoryCatalog.ReadBundled(path).Count == 0,
            "un historique local corrompu doit être ignoré");
    }
    finally { SafeDelete(root); }
});

Run("notes: grand historique borné et trois lignes dynamiques disponibles", () =>
{
    var notes = Enumerable.Range(1, 35)
        .Select(i => SampleReleaseNote("0.14.2." + i, "raidpilot", "2026-10-23", "Note " + i))
        .ToList();
    var cleaned = ReleaseHistoryCatalog.Normalize(notes);
    Require(cleaned.Count == 24 && cleaned.Take(3).Count() == 3,
        "le panneau pourrait dépasser le budget d'affichage");
});

if (failures.Count == 0)
{
    Console.WriteLine("40/40 tests launcher réussis.");
    return 0;
}
Console.Error.WriteLine($"{failures.Count}/40 test(s) launcher en échec");
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

static ReleaseHistoryEntry SampleReleaseNote(
    string version, string component, string date, string summary) => new()
{
    Component = component,
    Version = version,
    RaidPilotVersion = "0.14.2.18",
    BeaconVersion = "0.3.6",
    LauncherVersion = "0.2.13",
    Date = date,
    Summary = summary,
    Details = "Modification détaillée et compatible avec les versions précédentes."
};

internal sealed class SimulatedPartialDownloadHandler : HttpMessageHandler
{
    private readonly byte[] _bytes;
    private readonly bool _ignoreRange;
    private readonly int _firstBytes;
    internal int RequestCount { get; private set; }
    internal bool ObservedRange { get; private set; }

    internal SimulatedPartialDownloadHandler(byte[] bytes, bool ignoreRange)
    {
        _bytes = bytes;
        _ignoreRange = ignoreRange;
        _firstBytes = Math.Max(1, bytes.Length / 3);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (RequestCount == 1)
        {
            if (request.Headers.Range is not null)
                throw new IOException("Initial download must not use Range.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_bytes[.._firstBytes]),
            });
        }

        var start = request.Headers.Range?.Ranges.SingleOrDefault()?.From;
        ObservedRange = start == _firstBytes;
        if (!ObservedRange)
            throw new IOException("The resume must begin after the initial bytes.");
        if (_ignoreRange)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_bytes),
            });
        }

        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(_bytes[_firstBytes..]),
        };
        response.Content.Headers.ContentRange =
            new ContentRangeHeaderValue(_firstBytes, _bytes.Length - 1, _bytes.Length);
        return Task.FromResult(response);
    }
}
