using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Conserve la dernière réponse tarkov.dev valide afin que les extractions et
/// les quêtes restent disponibles pendant une panne réseau temporaire.
/// </summary>
internal sealed class TarkovDevJsonCache
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _rootDirectory;
    private readonly string? _bundledDirectory;

    internal TarkovDevJsonCache(string? rootDirectory = null, string? bundledDirectory = null)
    {
        _bundledDirectory = bundledDirectory ?? (rootDirectory is null
            ? Path.Combine(AppContext.BaseDirectory, "LocalMap", "cache") : null);
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaidPilot",
            "catalog-cache");
    }

    internal async Task<string> GetOrFetchAsync(
        string cacheKey,
        long maximumBytes,
        string? requiredDataProperty,
        Func<CancellationToken, Task<string>> fetchAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(fetchAsync);
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        if (cacheKey.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException("La clé de cache contient un caractère non permis.", nameof(cacheKey));
        }

        var cachePath = Path.Combine(_rootDirectory, $"{cacheKey}.json");
        var gate = FileLocks.GetOrAdd(cachePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A complete release catalogue is available immediately during a raid.
            // An unsupported mode never borrows quests from another game mode.
            if (_bundledDirectory is not null)
            {
                var mode = cacheKey.StartsWith("regular-", StringComparison.Ordinal) ? "regular"
                    : cacheKey.StartsWith("pve-", StringComparison.Ordinal) ? "pve" : null;
                if (mode is not null)
                {
                    var endpoint = cacheKey[(mode.Length + 1)..].Replace('-', '_');
                    var folder = mode == "regular" ? _bundledDirectory : Path.Combine(_bundledDirectory, mode);
                    var bundled = await TryLoadAsync(Path.Combine(folder, endpoint + ".json"),
                        maximumBytes, requiredDataProperty, cancellationToken).ConfigureAwait(false);
                    if (bundled is not null) return bundled;
                }
            }
            try
            {
                var downloaded = await fetchAsync(cancellationToken).ConfigureAwait(false);
                Validate(downloaded, maximumBytes, requiredDataProperty);
                await TrySaveAsync(cachePath, downloaded, cancellationToken).ConfigureAwait(false);
                return downloaded;
            }
            catch (Exception exception) when (IsRecoverableFetchFailure(exception, cancellationToken))
            {
                var cached = await TryLoadAsync(
                        cachePath,
                        maximumBytes,
                        requiredDataProperty,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (cached is not null)
                {
                    return cached;
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsRecoverableFetchFailure(
        Exception exception,
        CancellationToken cancellationToken)
    {
        return exception is HttpRequestException
            or IOException
            or JsonException
            or InvalidDataException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
    }

    private static void Validate(
        string json,
        long maximumBytes,
        string? requiredDataProperty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (Encoding.UTF8.GetByteCount(json) > maximumBytes)
        {
            throw new InvalidDataException("La réponse tarkov.dev dépasse la taille permise.");
        }

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                MaxDepth = 128,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
        if (document.RootElement.ValueKind is not JsonValueKind.Object
            || !document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("La réponse tarkov.dev ne contient pas un objet data valide.");
        }

        if (!string.IsNullOrWhiteSpace(requiredDataProperty)
            && (!data.TryGetProperty(requiredDataProperty, out var required)
                || required.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)))
        {
            throw new JsonException(
                $"La réponse tarkov.dev ne contient pas data.{requiredDataProperty}.");
        }
    }

    private static async Task<string?> TryLoadAsync(
        string cachePath,
        long maximumBytes,
        string? requiredDataProperty,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(cachePath);
            if (!file.Exists || file.Length <= 0 || file.Length > maximumBytes)
            {
                return null;
            }

            var cached = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
            Validate(cached, maximumBytes, requiredDataProperty);
            return cached;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException)
        {
            return null;
        }
    }

    private static async Task TrySaveAsync(
        string cachePath,
        string json,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(
                    temporaryPath,
                    json,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Le réseau reste prioritaire : une impossibilité d'écrire le
            // cache ne doit pas empêcher l'overlay d'utiliser la réponse.
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
