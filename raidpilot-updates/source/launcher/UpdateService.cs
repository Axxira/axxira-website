using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace RaidPilot.Launcher;

internal sealed class UpdateService : IDisposable
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const long MaximumArchiveBytes = 1024L * 1024 * 1024;
    private const long MaximumExtractedBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumZipEntries = 20_000;
    internal const int AutomaticUpdateCheckIntervalMilliseconds = 120_000;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    internal UpdateService(HttpClient? client = null)
    {
        _ownsHttpClient = client is null;
        _httpClient = client ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        var currentVersion = typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.2.15";
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("RaidPilot-Launcher", currentVersion));
    }

    internal static bool IsAllowedManifestLocation(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }
        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsNewer(string? installedVersion, string? availableVersion)
    {
        var installedCore = NormalizeVersionCore(installedVersion);
        var availableCore = NormalizeVersionCore(availableVersion);

        if (!Version.TryParse(availableCore, out var available))
        {
            return false;
        }

        if (!Version.TryParse(installedCore, out var installed))
        {
            return true;
        }

        return available > installed;
    }

    private static string? NormalizeVersionCore(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return version.Split(new[] { '+', '-' }, 2)[0];
    }

    internal static string GetLauncherUpdaterWorkingDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaidPilot Launcher",
            "Updater");
    }

    internal static string NormalizeVersionForDisplay(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "—";
        }

        var core = NormalizeVersionCore(version) ?? version;
        if (Version.TryParse(core, out var parsed))
        {
            if (parsed.Revision > 0)
            {
                return parsed.ToString(4);
            }
            return parsed.Build > 0
                ? $"{parsed.Major}.{parsed.Minor}.{parsed.Build}"
                : $"{parsed.Major}.{parsed.Minor}";
        }

        return core;
    }

    internal static bool HasAvailableUpdate(
        string? installedRaidPilot,
        string? installedBeacon,
        string? installedLauncher,
        UpdateManifest? manifest)
    {
        if (manifest is null)
        {
            return false;
        }

        return manifest.RaidPilot is { } raidPilot
                && IsNewer(installedRaidPilot, raidPilot.Version)
            || manifest.Beacon is { } beacon
                && IsNewer(installedBeacon, beacon.Version)
            || manifest.Launcher is { } launcher
                && IsNewer(installedLauncher, launcher.Version);
    }

    internal static string? GetInstalledVersion(string exePath)
    {
        try
        {
            if (!File.Exists(exePath))
            {
                return null;
            }
            var info = FileVersionInfo.GetVersionInfo(exePath);
            return string.IsNullOrWhiteSpace(info.ProductVersion)
                ? info.FileVersion
                : info.ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    internal static Uri BuildFreshManifestUri(Uri uri, long cacheToken)
    {
        if (uri.IsFile || !uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var previousQuery = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(previousQuery)
            ? $"raidpilot_refresh={cacheToken}"
            : $"{previousQuery}&raidpilot_refresh={cacheToken}";
        return builder.Uri;
    }

    internal async Task<UpdateManifest> LoadManifestAsync(
        string manifestLocation,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(manifestLocation, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "La source de mises à jour doit être une adresse HTTPS ou file:// valide.");
        }

        byte[] bytes;
        if (uri.IsFile)
        {
            var file = new FileInfo(uri.LocalPath);
            if (!file.Exists || file.Length is <= 0 or > MaximumManifestBytes)
            {
                throw new InvalidOperationException("Le manifeste local est absent ou trop volumineux.");
            }
            bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
        }
        else
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                BuildFreshManifestUri(uri, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            request.Headers.Pragma.ParseAdd("no-cache");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumManifestBytes)
            {
                throw new InvalidOperationException("Le manifeste distant dépasse la taille autorisée.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (memory.Length + read > MaximumManifestBytes)
                {
                    throw new InvalidOperationException("Le manifeste distant dépasse la taille autorisée.");
                }
                memory.Write(buffer, 0, read);
            }
            bytes = memory.ToArray();
        }

        UpdateManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(
                bytes,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    MaxDepth = 8,
                }) ?? throw new JsonException("Manifeste vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Le manifeste de mise à jour est invalide.", exception);
        }

        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Version de manifeste non prise en charge : {manifest.SchemaVersion}.");
        }

        ValidateComponent(manifest.RaidPilot, "RaidPilot");
        ValidateComponent(manifest.Beacon, "Beacon");
        ValidateComponent(manifest.Launcher, "Launcher");
        ValidateOfficialStableManifestSources(manifestLocation, manifest);
        return manifest;
    }

    internal async Task InstallComponentAsync(
        UpdateComponent component,
        string targetDirectory,
        string expectedExecutable,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ValidateComponent(component, expectedExecutable);
        Directory.CreateDirectory(LauncherPaths.Downloads);
        Directory.CreateDirectory(LauncherPaths.Apps);

        var workId = Guid.NewGuid().ToString("N");
        var archivePath = Path.Combine(
            LauncherPaths.Downloads,
            $"{Path.GetFileNameWithoutExtension(expectedExecutable)}-{component.Version}-{workId}.zip");
        var staging = Path.Combine(
            LauncherPaths.Apps,
            $".staging-{Path.GetFileName(targetDirectory)}-{workId}");
        var previous = targetDirectory + ".previous";

        try
        {
            await DownloadFileAsync(
                component.Url,
                archivePath,
                component.SizeBytes,
                progress,
                cancellationToken);
            progress?.Report(72);

            var hash = await ComputeSha256Async(archivePath, cancellationToken);
            if (!string.Equals(hash, component.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SHA-256 invalide. Attendu {component.Sha256}, reçu {hash}.");
            }

            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
            Directory.CreateDirectory(staging);
            ValidateZipArchive(archivePath);
            ZipFile.ExtractToDirectory(archivePath, staging, overwriteFiles: true);
            progress?.Report(82);

            var payloadRoot = ResolvePayloadRoot(staging, expectedExecutable);
            var executable = Path.Combine(payloadRoot, expectedExecutable);
            if (!File.Exists(executable))
            {
                throw new InvalidOperationException(
                    $"L’archive ne contient pas {expectedExecutable}.");
            }
            VerifyNativePayloadVersion(
                executable, expectedExecutable, component.Version);

            PreservePortableUserData(targetDirectory, payloadRoot);

            if (Directory.Exists(previous))
            {
                Directory.Delete(previous, recursive: true);
            }

            var hadCurrent = Directory.Exists(targetDirectory);
            if (hadCurrent)
            {
                Directory.Move(targetDirectory, previous);
            }

            try
            {
                if (string.Equals(
                        Path.GetFullPath(payloadRoot).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Move(staging, targetDirectory);
                }
                else
                {
                    Directory.Move(payloadRoot, targetDirectory);
                    TryDeleteDirectory(staging);
                }
            }
            catch
            {
                TryDeleteDirectory(targetDirectory);
                if (hadCurrent && Directory.Exists(previous))
                {
                    Directory.Move(previous, targetDirectory);
                }
                throw;
            }

            progress?.Report(96);
            if (Directory.Exists(previous))
            {
                // Keep exactly one rollback copy. It is intentionally retained
                // after a successful update and replaced by the next update.
            }
            progress?.Report(100);
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(staging);
        }
    }

    internal async Task PrepareLauncherSelfUpdateAsync(
        UpdateComponent component,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ValidateComponent(component, "RaidPilot.Launcher.exe");
        Directory.CreateDirectory(LauncherPaths.Downloads);

        var workId = Guid.NewGuid().ToString("N");
        var archivePath = Path.Combine(
            LauncherPaths.Downloads,
            $"RaidPilot.Launcher-{component.Version}-{workId}.zip");

        await DownloadFileAsync(
            component.Url,
            archivePath,
            component.SizeBytes,
            progress,
            cancellationToken);
        progress?.Report(72);

        var hash = await ComputeSha256Async(archivePath, cancellationToken);
        if (!string.Equals(hash, component.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteFile(archivePath);
            throw new InvalidOperationException(
                $"SHA-256 du launcher invalide. Attendu {component.Sha256}, reçu {hash}.");
        }

        ValidateZipArchive(archivePath);
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            var hasLauncher = archive.Entries.Any(entry =>
                string.Equals(
                    Path.GetFileName(entry.FullName),
                    "RaidPilot.Launcher.exe",
                    StringComparison.OrdinalIgnoreCase));
            if (!hasLauncher)
            {
                TryDeleteFile(archivePath);
                throw new InvalidOperationException(
                    "Le package launcher ne contient pas RaidPilot.Launcher.exe.");
            }
        }

        var scriptPath = Path.Combine(
            LauncherPaths.Downloads,
            $"apply-launcher-{component.Version}-{workId}.ps1");
        var script = BuildLauncherSelfUpdateScript();
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        var targetDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var updaterWorkingDirectory = GetLauncherUpdaterWorkingDirectory();
        Directory.CreateDirectory(updaterWorkingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = updaterWorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-Archive");
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add("-Target");
        startInfo.ArgumentList.Add(targetDirectory);
        startInfo.ArgumentList.Add("-Executable");
        startInfo.ArgumentList.Add("RaidPilot.Launcher.exe");
        startInfo.ArgumentList.Add("-ExpectedVersion");
        startInfo.ArgumentList.Add(component.Version);
        startInfo.ArgumentList.Add("-ExpectedSha256");
        startInfo.ArgumentList.Add(component.Sha256);
        startInfo.ArgumentList.Add("-UpdaterWork");
        startInfo.ArgumentList.Add(updaterWorkingDirectory);

        if (Process.Start(startInfo) is null)
        {
            TryDeleteFile(scriptPath);
            TryDeleteFile(archivePath);
            throw new InvalidOperationException(
                "Impossible de lancer le programme de mise à jour du launcher.");
        }

        progress?.Report(100);
    }

    internal static string BuildLauncherSelfUpdateScript() => """
param(
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][string]$Archive,
    [Parameter(Mandatory=$true)][string]$Target,
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$ExpectedVersion,
    [Parameter(Mandatory=$true)][string]$ExpectedSha256,
    [Parameter(Mandatory=$true)][string]$UpdaterWork
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $UpdaterWork)) {
    New-Item -ItemType Directory -Force -Path $UpdaterWork | Out-Null
}
Set-Location -LiteralPath $UpdaterWork
[Environment]::CurrentDirectory = $UpdaterWork

$logDirectory = Join-Path $env:LOCALAPPDATA 'RaidPilot Launcher'
$logPath = Join-Path $logDirectory 'self-update.log'
try { New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null } catch {}
function Write-UpdateLog([string]$Message) {
    try {
        $line = ('{0:o} {1}' -f [DateTimeOffset]::Now, $Message)
        Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    } catch {}
}

$staging = $Target + '.update-' + [Guid]::NewGuid().ToString('N')
$previous = $Target + '.previous'
Write-UpdateLog ("START pid=$ProcessId target=$Target expected=$ExpectedVersion cwd=" + (Get-Location).Path)
try {
    try { Wait-Process -Id $ProcessId -Timeout 90 -ErrorAction SilentlyContinue } catch {}
    Start-Sleep -Milliseconds 600

    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    # Verify the archive again AFTER the original launcher has exited.
    # This closes the check-to-use window after the C# hash verification.
    $archiveHash = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash
    if (-not [string]::Equals($archiveHash, $ExpectedSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Archive launcher modifiée après téléchargement (SHA-256)."
    }
    Expand-Archive -LiteralPath $Archive -DestinationPath $staging -Force

    $payload = $staging
    $candidate = Join-Path $payload $Executable
    if (-not (Test-Path -LiteralPath $candidate)) {
        $dirs = @(Get-ChildItem -LiteralPath $staging -Directory)
        $files = @(Get-ChildItem -LiteralPath $staging -File)
        if ($files.Count -eq 0 -and $dirs.Count -eq 1) {
            $nested = Join-Path $dirs[0].FullName $Executable
            if (Test-Path -LiteralPath $nested) {
                $payload = $dirs[0].FullName
                $candidate = $nested
            }
        }
    }

    if (-not (Test-Path -LiteralPath $candidate)) {
        throw "Package launcher invalide : $Executable absent."
    }

    $actualVersion = (Get-Item -LiteralPath $candidate).VersionInfo.ProductVersion
    $actualCore = ($actualVersion -split '[+-]', 2)[0]
    $expectedCore = ($ExpectedVersion -split '[+-]', 2)[0]
    $actualParsed = $null
    $expectedParsed = $null
    $actualValid = [Version]::TryParse($actualCore, [ref]$actualParsed)
    $expectedValid = [Version]::TryParse($expectedCore, [ref]$expectedParsed)
    if (-not $actualValid -or -not $expectedValid -or $actualParsed -ne $expectedParsed) {
        throw "Version launcher inattendue : $actualVersion (attendu $ExpectedVersion)."
    }

    if (Test-Path -LiteralPath $previous) {
        Remove-Item -LiteralPath $previous -Recurse -Force
    }

    $hadCurrent = Test-Path -LiteralPath $Target
    if ($hadCurrent) {
        Move-Item -LiteralPath $Target -Destination $previous
    }

    try {
        Move-Item -LiteralPath $payload -Destination $Target
        if ($payload -ne $staging -and (Test-Path -LiteralPath $staging)) {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    }
    catch {
        if (Test-Path -LiteralPath $Target) {
            Remove-Item -LiteralPath $Target -Recurse -Force
        }
        if ($hadCurrent -and (Test-Path -LiteralPath $previous)) {
            Move-Item -LiteralPath $previous -Destination $Target
        }
        throw
    }

    $newExe = Join-Path $Target $Executable
    if (-not (Test-Path -LiteralPath $newExe)) {
        throw "Launcher installé mais exécutable introuvable : $newExe"
    }

    $relaunch = $null
    try {
        $relaunch = Start-Process -FilePath $newExe -WorkingDirectory $Target -PassThru -ErrorAction Stop
        Start-Sleep -Milliseconds 1200
        if ($relaunch.HasExited) {
            throw "Le nouveau launcher s'est fermé immédiatement après la mise à jour."
        }
    }
    catch {
        $relaunchError = $_
        try {
            if ($relaunch -and -not $relaunch.HasExited) {
                $relaunch.Kill()
                $relaunch.WaitForExit()
            }
        } catch {}

        # Une mise à jour du launcher ne doit jamais laisser l'utilisateur
        # sans application. Si le nouveau binaire ne se relance pas, restaure
        # la copie précédente et tente de rouvrir celle-ci.
        if (Test-Path -LiteralPath $Target) {
            Remove-Item -LiteralPath $Target -Recurse -Force
        }
        if ($hadCurrent -and (Test-Path -LiteralPath $previous)) {
            Move-Item -LiteralPath $previous -Destination $Target
        }
        throw $relaunchError
    }

    Write-UpdateLog ("SUCCESS version=$actualVersion path=$newExe")
}
catch {
    $failure = $_
    Write-UpdateLog ("ERROR " + $failure.Exception.ToString())

    # Si n'importe quelle étape échoue après la fermeture du Launcher,
    # restaure l'ancienne copie si nécessaire puis rouvre une version
    # fonctionnelle. L'utilisateur ne doit jamais rester sans application.
    try {
        if (-not (Test-Path -LiteralPath $Target) -and (Test-Path -LiteralPath $previous)) {
            Move-Item -LiteralPath $previous -Destination $Target
            Write-UpdateLog "ROLLBACK restored previous directory"
        }
    } catch {
        Write-UpdateLog ("ROLLBACK_ERROR " + $_.Exception.Message)
    }

    $fallbackExe = Join-Path $Target $Executable
    if (Test-Path -LiteralPath $fallbackExe) {
        try {
            Start-Process -FilePath $fallbackExe -WorkingDirectory $Target -ErrorAction Stop
            Write-UpdateLog ("FALLBACK_RELAUNCH " + $fallbackExe)
        } catch {
            Write-UpdateLog ("FALLBACK_RELAUNCH_ERROR " + $_.Exception.Message)
        }
    }

    throw $failure
}
finally {
    try { if (Test-Path -LiteralPath $Archive) { Remove-Item -LiteralPath $Archive -Force } } catch {}
    try { if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force } } catch {}
    try { Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force } catch {}
}
""";

    internal static void EnsureBundledPayloadInstalled()
    {
        Directory.CreateDirectory(LauncherPaths.Apps);
        EnsureBundledComponentInstalled(
            LauncherPaths.BundledRaidPilot,
            LauncherPaths.RaidPilot,
            "RaidPilot.Overlay.exe");
        EnsureBundledComponentInstalled(
            LauncherPaths.BundledBeacon,
            LauncherPaths.Beacon,
            "RaidPilot.Beacon.exe");
    }

    internal static bool ShouldInstallBundledVersion(
        string? installedVersion,
        string? bundledVersion)
    {
        if (!Version.TryParse(NormalizeVersionCore(bundledVersion), out _))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(installedVersion)
            || IsNewer(installedVersion, bundledVersion);
    }

    internal static void EnsureBundledComponentInstalled(
        string bundledDirectory,
        string targetDirectory,
        string executableName)
    {
        // Recover after power loss between moving .previous and installing
        // the candidate. This must precede any bundled-version comparison.
        RecoverInterruptedComponent(targetDirectory, executableName);
        RecoverPreviousUserData(targetDirectory);
        var bundledExecutable = Path.Combine(bundledDirectory, executableName);
        if (!Directory.Exists(bundledDirectory)
            || !File.Exists(bundledExecutable))
        {
            return;
        }

        var targetExecutable = Path.Combine(targetDirectory, executableName);
        var installedVersion = GetInstalledVersion(targetExecutable);
        var bundledVersion = GetInstalledVersion(bundledExecutable);
        if (!ShouldInstallBundledVersion(installedVersion, bundledVersion))
        {
            return;
        }

        var staging = targetDirectory + ".bundled-staging-" + Guid.NewGuid().ToString("N");
        var previous = targetDirectory + ".previous";

        try
        {
            CopyDirectory(bundledDirectory, staging);
            PreservePortableUserData(targetDirectory, staging);

            if (Directory.Exists(previous))
            {
                TryDeleteDirectory(previous);
            }

            var hadCurrent = Directory.Exists(targetDirectory);
            if (hadCurrent)
            {
                Directory.Move(targetDirectory, previous);
            }

            try
            {
                Directory.Move(staging, targetDirectory);
            }
            catch
            {
                TryDeleteDirectory(targetDirectory);
                if (hadCurrent && Directory.Exists(previous))
                {
                    Directory.Move(previous, targetDirectory);
                }
                throw;
            }
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    internal static bool RecoverInterruptedComponent(
        string targetDirectory, string executableName)
    {
        if (File.Exists(Path.Combine(targetDirectory, executableName)))
        {
            return false;
        }
        var previous = targetDirectory + ".previous";
        if (!File.Exists(Path.Combine(previous, executableName)))
        {
            return false;
        }
        var incomplete = targetDirectory + ".incomplete-" + Guid.NewGuid().ToString("N");
        var movedIncomplete = false;
        if (Directory.Exists(targetDirectory))
        {
            Directory.Move(targetDirectory, incomplete);
            movedIncomplete = true;
        }
        try
        {
            Directory.Move(previous, targetDirectory);
            if (movedIncomplete) TryDeleteDirectory(incomplete);
            return true;
        }
        catch
        {
            if (movedIncomplete && !Directory.Exists(targetDirectory)
                && Directory.Exists(incomplete))
            {
                Directory.Move(incomplete, targetDirectory);
            }
            throw;
        }
    }

    internal static void RecoverPreviousUserData(string targetDirectory)
    {
        var target = Path.Combine(targetDirectory, "UserData");
        var previous = Path.Combine(targetDirectory + ".previous", "UserData");
        if (!Directory.Exists(targetDirectory) || Directory.Exists(target)
            || !Directory.Exists(previous))
        {
            return;
        }

        // Copy completely before making it visible; retain the rollback copy.
        var staging = targetDirectory + ".profile-recovery-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyDirectory(previous, staging);
            Directory.Move(staging, target);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    internal static bool RollbackOrRemove(
        string targetDirectory, string expectedExecutable, bool existedBefore)
    {
        if (existedBefore)
            return Rollback(targetDirectory, expectedExecutable);
        try
        {
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
            return !Directory.Exists(targetDirectory);
        }
        catch { return false; }
    }

    internal static bool Rollback(string targetDirectory, string expectedExecutable)
    {
        var previous = targetDirectory + ".previous";
        if (!Directory.Exists(previous)
            || !File.Exists(Path.Combine(previous, expectedExecutable)))
        {
            return false;
        }

        var failed = targetDirectory + ".failed-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Move(targetDirectory, failed);
            }
            Directory.Move(previous, targetDirectory);
            TryDeleteDirectory(failed);
            return true;
        }
        catch
        {
            try
            {
                if (!Directory.Exists(targetDirectory) && Directory.Exists(failed))
                {
                    Directory.Move(failed, targetDirectory);
                }
            }
            catch
            {
            }
            return false;
        }
    }

    /// <summary>
    /// Retry a network interruption using HTTP Range where supported.
    /// A server that ignores Range triggers a safe full-file restart instead
    /// of appending inconsistent bytes. The SHA-256 is verified by the caller.
    /// </summary>
    private async Task DownloadFileAsync(
        string location,
        string destination,
        long? expectedSize,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("URL de package invalide.");
        }
        if (expectedSize is > MaximumArchiveBytes)
            throw new InvalidOperationException("Archive de mise à jour trop volumineuse.");

        if (uri.IsFile)
        {
            await using var source = new FileStream(
                uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var target = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None);
            var copied = await CopyWithProgressAsync(source, target,
                source.Length, progress, cancellationToken);
            if (expectedSize is > 0 && copied != expectedSize.Value)
                throw new InvalidOperationException("Taille de package locale inattendue.");
            return;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = File.Exists(destination) ? new FileInfo(destination).Length : 0;
            if (offset > MaximumArchiveBytes
                || (expectedSize is > 0 && offset > expectedSize.Value))
            {
                File.Delete(destination);
                offset = 0;
            }
            if (offset > 0 && expectedSize is > 0 && offset == expectedSize.Value)
                return; // Caller must still validate the entire SHA-256.

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (offset > 0)
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (offset > 0
                    && response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    File.Delete(destination);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var isRange = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (isRange)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (offset <= 0 || range?.From != offset
                        || (expectedSize is > 0 && range.Length.HasValue
                            && range.Length.Value != expectedSize.Value))
                    {
                        throw new InvalidOperationException(
                            "Réponse de reprise HTTP incohérente : archive refusée.");
                    }
                }
                else if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new InvalidOperationException(
                        "Réponse HTTP inattendue pour le téléchargement.");
                }

                // A 200 response to a Range request means the server ignored
                // the range. Recreate the file; never append duplicate bytes.
                var baseLength = isRange ? offset : 0L;
                var length = expectedSize
                    ?? response.Content.Headers.ContentRange?.Length
                    ?? (response.Content.Headers.ContentLength + baseLength);
                if (length is > MaximumArchiveBytes
                    || response.Content.Headers.ContentLength is > MaximumArchiveBytes)
                    throw new InvalidOperationException("Archive réseau trop volumineuse.");

                await using var sourceStream = await response.Content.ReadAsStreamAsync(
                    cancellationToken);
                await using var destinationStream = new FileStream(
                    destination,
                    isRange ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var total = await CopyWithProgressAsync(
                    sourceStream, destinationStream, length,
                    progress, cancellationToken, baseLength);
                if (expectedSize is > 0 && total != expectedSize.Value)
                    throw new IOException("Réponse incomplète : reprise de la même archive.");
                return;
            }
            catch (Exception error) when (attempt < 3
                && !cancellationToken.IsCancellationRequested
                && (error is IOException or HttpRequestException or OperationCanceledException))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 400),
                    cancellationToken);
            }
        }
        throw new IOException("Téléchargement interrompu malgré les tentatives de reprise.");
    }

    private static async Task<long> CopyWithProgressAsync(
        Stream source,
        Stream destination,
        long? totalLength,
        IProgress<int>? progress,
        CancellationToken cancellationToken,
        long initialBytes = 0)
    {
        var buffer = new byte[64 * 1024];
        long total = initialBytes;
        // Unlike HttpClient's header timeout, this timer protects streamed
        // data against a stalled connection that never closes.
        using var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (true)
        {
            idleTimeout.CancelAfter(TimeSpan.FromSeconds(60));
            var read = await source.ReadAsync(buffer, idleTimeout.Token);
            if (read == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), idleTimeout.Token);
            total += read;
            if (total > MaximumArchiveBytes)
                throw new InvalidOperationException("Téléchargement interrompu : taille maximale dépassée.");
            if (totalLength is > 0)
            {
                var percent = (int)Math.Clamp(total * 70 / totalLength.Value, 0, 70);
                progress?.Report(percent);
            }
        }
        await destination.FlushAsync(cancellationToken);
        return total;
    }

    internal static void ValidateZipArchive(string archivePath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > MaximumZipEntries)
            throw new InvalidOperationException("Archive invalide : trop de fichiers.");
        long expanded = 0;
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(normalized)
                || normalized.StartsWith("/", StringComparison.Ordinal)
                || normalized.Contains(':', StringComparison.Ordinal)
                || normalized.Split('/').Any(s => s is ".." or ".")
                || !unique.Add(normalized.TrimEnd('/')))
            {
                throw new InvalidOperationException("Archive invalide : chemin dangereux ou en double.");
            }
            if (entry.Length < 0
                || entry.Length > MaximumArchiveBytes
                || expanded > MaximumExtractedBytes - entry.Length)
            {
                throw new InvalidOperationException("Archive invalide : décompression excessive.");
            }
            expanded += entry.Length;
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ResolvePayloadRoot(string staging, string expectedExecutable)
    {
        if (File.Exists(Path.Combine(staging, expectedExecutable)))
        {
            return staging;
        }

        var directories = Directory.GetDirectories(staging);
        var files = Directory.GetFiles(staging);
        if (files.Length == 0 && directories.Length == 1
            && File.Exists(Path.Combine(directories[0], expectedExecutable)))
        {
            return directories[0];
        }
        return staging;
    }

    /// <summary>
    /// Restrict the built-in Stable feed to the exact repository's immutable
    /// GitHub release assets. Custom feeds remain explicitly opt-in for
    /// development / local tests; this is not a digital signature.
    /// </summary>
    internal static void ValidateOfficialStableManifestSources(
        string manifestLocation, UpdateManifest manifest)
    {
        if (!string.Equals(manifestLocation, LauncherSettings.StableManifestUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        string? sharedTag = null;
        var components = new[]
        {
            (Name: "Overlay", Component: manifest.RaidPilot,
                Executable: "RaidPilot-Overlay"),
            (Name: "Beacon", Component: manifest.Beacon,
                Executable: "RaidPilot-Beacon"),
            (Name: "Launcher", Component: manifest.Launcher,
                Executable: "RaidPilot-Launcher"),
        };
        foreach (var item in components)
        {
            if (item.Component is not { } component)
                continue;
            if (!Uri.TryCreate(component.Url, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("URL Stable invalide.");
            var parts = uri.AbsolutePath.Split('/');
            var expectedFile = item.Executable + "-v" + component.Version + "-Windows-x64.zip";
            if (uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || uri.UserInfo.Length != 0 || uri.Query.Length != 0
                || uri.Fragment.Length != 0 || parts.Length != 7
                || !string.Equals(parts[1], "Axxira", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(parts[2], "axxira-website", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(parts[3], "releases", StringComparison.Ordinal)
                || !string.Equals(parts[4], "download", StringComparison.Ordinal)
                || !parts[5].StartsWith("raidpilot-stable-rp", StringComparison.Ordinal)
                || !string.Equals(parts[6], expectedFile, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Publication Stable non autorisée pour " + item.Name + ".");
            }
            if (sharedTag is null) sharedTag = parts[5];
            else if (!string.Equals(sharedTag, parts[5], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Les trois composants Stable doivent provenir de la même publication.");
            }
        }
    }

    internal static void VerifyNativePayloadVersion(
        string extractedExecutable, string expectedExecutable, string expectedVersion)
    {
        if (expectedExecutable is not ("RaidPilot.Overlay.exe" or "RaidPilot.Beacon.exe"))
            return; // Synthetic tests can still use Dummy.exe payloads.
        var actual = NormalizeVersionCore(GetInstalledVersion(extractedExecutable));
        if (!Version.TryParse(actual, out var parsedActual)
            || !Version.TryParse(NormalizeVersionCore(expectedVersion), out var parsedExpected)
            || parsedActual != parsedExpected)
        {
            throw new InvalidOperationException(
                $"Version binaire incorrecte dans l’archive {expectedExecutable}: "
                + $"{actual ?? "inconnue"} (attendu {expectedVersion}).");
        }
    }

    private static void ValidateComponent(UpdateComponent? component, string name)
    {
        if (component is null)
        {
            return;
        }
        if (!Version.TryParse(component.Version, out _)
            || !Uri.TryCreate(component.Url, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            || component.Sha256.Length != 64
            || component.Sha256.Any(c => !Uri.IsHexDigit(c))
            || component.SizeBytes is < 1 or > MaximumArchiveBytes)
        {
            throw new InvalidOperationException($"Composant de mise à jour invalide : {name}.");
        }
    }

    private static void PreservePortableUserData(string currentDirectory, string stagedDirectory)
    {
        // WebView2 stores map filters and manual map calibration in this
        // portable profile. Copy it before swapping the binaries so both
        // the new installation and the rollback retain the user's settings.
        var profile = Path.Combine(currentDirectory, "UserData");
        if (Directory.Exists(profile))
        {
            CopyDirectory(profile, Path.Combine(stagedDirectory, "UserData"));
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(destination, Path.GetFileName(file)),
                overwrite: true);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    public void Dispose() { if (_ownsHttpClient) _httpClient.Dispose(); }
}
