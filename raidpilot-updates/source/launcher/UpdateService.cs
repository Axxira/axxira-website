using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace RaidPilot.Launcher;

internal sealed class UpdateService : IDisposable
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    internal UpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("RaidPilot-Launcher", "0.2.4"));
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
        if (!Version.TryParse(installedVersion, out var installed)
            || !Version.TryParse(availableVersion, out var available))
        {
            return !string.Equals(
                installedVersion,
                availableVersion,
                StringComparison.OrdinalIgnoreCase)
            && Version.TryParse(availableVersion, out _);
        }
        return available > installed;
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
            using var response = await _httpClient.GetAsync(
                uri,
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
            ZipFile.ExtractToDirectory(archivePath, staging, overwriteFiles: true);
            progress?.Report(82);

            var payloadRoot = ResolvePayloadRoot(staging, expectedExecutable);
            var executable = Path.Combine(payloadRoot, expectedExecutable);
            if (!File.Exists(executable))
            {
                throw new InvalidOperationException(
                    $"L’archive ne contient pas {expectedExecutable}.");
            }

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
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
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
    [Parameter(Mandatory=$true)][string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
$staging = $Target + '.update-' + [Guid]::NewGuid().ToString('N')
$previous = $Target + '.previous'
try {
    try { Wait-Process -Id $ProcessId -Timeout 90 -ErrorAction SilentlyContinue } catch {}
    Start-Sleep -Milliseconds 600

    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
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
    if ($actualVersion -ne $ExpectedVersion) {
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
            $rollbackExe = Join-Path $Target $Executable
            if (Test-Path -LiteralPath $rollbackExe) {
                try {
                    Start-Process -FilePath $rollbackExe -WorkingDirectory $Target -ErrorAction SilentlyContinue
                } catch {}
            }
        }
        throw $relaunchError
    }
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
        if (!Version.TryParse(bundledVersion, out _))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(installedVersion)
            || IsNewer(installedVersion, bundledVersion);
    }

    private static void EnsureBundledComponentInstalled(
        string bundledDirectory,
        string targetDirectory,
        string executableName)
    {
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

        if (uri.IsFile)
        {
            await using var source = new FileStream(
                uri.LocalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            await using var target = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            await CopyWithProgressAsync(
                source,
                target,
                source.Length,
                progress,
                cancellationToken);
            return;
        }

        using var response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength ?? expectedSize;
        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await CopyWithProgressAsync(
            sourceStream,
            destinationStream,
            length,
            progress,
            cancellationToken);
    }

    private static async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        long? totalLength,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            if (totalLength is > 0)
            {
                var percent = (int)Math.Clamp(total * 70 / totalLength.Value, 0, 70);
                progress?.Report(percent);
            }
        }
        await destination.FlushAsync(cancellationToken);
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
            || component.SizeBytes is < 1)
        {
            throw new InvalidOperationException($"Composant de mise à jour invalide : {name}.");
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

    public void Dispose() => _httpClient.Dispose();
}