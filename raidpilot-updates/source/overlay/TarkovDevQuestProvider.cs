using System.Net.Http;
using System.Text.Json;

namespace RaidPilot.Overlay;

public sealed record QuestCandidate(
    string Id,
    string Name,
    string NormalizedName,
    IReadOnlyList<string> Aliases,
    int LocationCount)
{
    public IReadOnlyList<QuestContextObjective> Objectives { get; init; } = Array.Empty<QuestContextObjective>();
}

/// <summary>
/// Charge les quêtes publiées par json.tarkov.dev et conserve uniquement
/// celles qui possèdent au moins un objectif géolocalisé sur la carte active.
/// </summary>
public sealed class TarkovDevQuestProvider
{
    private const long MaximumResponseBytes = 24L * 1024L * 1024L;

    public static readonly Uri TasksUri = new("https://json.tarkov.dev/regular/tasks");
    public static readonly Uri FrenchTranslationsUri = new("https://json.tarkov.dev/regular/tasks_fr");
    public static readonly Uri EnglishTranslationsUri = new("https://json.tarkov.dev/regular/tasks_en");
    public static readonly Uri MapsUri = new("https://json.tarkov.dev/regular/maps");

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(18),
    };

    private readonly HttpClient _httpClient;
    private readonly TarkovDevJsonCache? _cache;

    public TarkovDevQuestProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _cache = httpClient is null ? new TarkovDevJsonCache() : null;
    }

    internal TarkovDevQuestProvider(HttpClient httpClient, TarkovDevJsonCache? cache)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache;
    }

    public async Task<IReadOnlyList<QuestCandidate>> GetForMapAsync(
        string normalizedMapName,
        string gameMode = "regular",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedMapName);
        if (gameMode is not ("regular" or "pve" or "pvp-season"))
        {
            throw new ArgumentOutOfRangeException(nameof(gameMode));
        }

        var tasksTask = LoadDocumentAsync(gameMode, "tasks", "tasks", cancellationToken);
        var frenchTask = LoadDocumentAsync(gameMode, "tasks_fr", null, cancellationToken);
        var englishTask = LoadDocumentAsync(gameMode, "tasks_en", null, cancellationToken);
        var mapsTask = LoadDocumentAsync(gameMode, "maps", "maps", cancellationToken);
        await Task.WhenAll(tasksTask, frenchTask, englishTask, mapsTask).ConfigureAwait(false);

        return Parse(
            await tasksTask.ConfigureAwait(false),
            await frenchTask.ConfigureAwait(false),
            await englishTask.ConfigureAwait(false),
            await mapsTask.ConfigureAwait(false),
            normalizedMapName);
    }

    private static Uri CreateEndpointUri(string gameMode, string endpoint)
    {
        return new Uri($"https://json.tarkov.dev/{gameMode}/{endpoint}");
    }

    private Task<string> LoadDocumentAsync(
        string gameMode,
        string endpoint,
        string? requiredDataProperty,
        CancellationToken cancellationToken)
    {
        var uri = CreateEndpointUri(gameMode, endpoint);
        return _cache is null
            ? DownloadStringAsync(uri, cancellationToken)
            : _cache.GetOrFetchAsync(
                $"{gameMode}-{endpoint.Replace('_', '-')}",
                MaximumResponseBytes,
                requiredDataProperty,
                token => DownloadStringAsync(uri, token),
                cancellationToken);
    }

    public static IReadOnlyList<QuestCandidate> Parse(
        string tasksJson,
        string frenchTranslationsJson,
        string englishTranslationsJson,
        string mapsJson,
        string normalizedMapName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tasksJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(frenchTranslationsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(englishTranslationsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedMapName);

        using var tasksDocument = JsonDocument.Parse(tasksJson);
        using var frenchDocument = JsonDocument.Parse(frenchTranslationsJson);
        using var englishDocument = JsonDocument.Parse(englishTranslationsJson);
        using var mapsDocument = JsonDocument.Parse(mapsJson);

        var french = ParseTranslations(frenchDocument.RootElement);
        var english = ParseTranslations(englishDocument.RootElement);
        var mapIds = FindMapIds(mapsDocument.RootElement, normalizedMapName.Trim());
        if (mapIds.Count == 0)
        {
            throw new KeyNotFoundException(
                $"La carte tarkov.dev '{normalizedMapName}' est introuvable.");
        }

        if (!TryGetObjectProperty(tasksDocument.RootElement, "data", out var data)
            || !data.TryGetProperty("tasks", out var tasks))
        {
            throw new JsonException("La réponse tarkov.dev ne contient pas data.tasks.");
        }

        var result = new List<QuestCandidate>();
        foreach (var task in EnumerateObjects(tasks))
        {
            if (!TryGetRequiredString(task, "id", out var id)
                || !TryGetRequiredString(task, "name", out var nameKey)
                || !task.TryGetProperty("objectives", out var objectives)
                || objectives.ValueKind is not JsonValueKind.Array)
            {
                continue;
            }

            var locationCount = CountLocationsOnMap(objectives, mapIds);
            if (locationCount == 0)
            {
                continue;
            }

            var normalizedName = TryGetOptionalString(task, "normalizedName") ?? string.Empty;
            var frenchName = french.TryGetValue(nameKey, out var translatedFrench)
                ? translatedFrench
                : string.Empty;
            var englishName = english.TryGetValue(nameKey, out var translatedEnglish)
                ? translatedEnglish
                : string.Empty;
            var displayName = FirstNonEmpty(frenchName, englishName, normalizedName, nameKey);
            var aliases = new[]
                {
                    displayName,
                    frenchName,
                    englishName,
                    normalizedName.Replace('-', ' '),
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            result.Add(new QuestCandidate(id, displayName, normalizedName, aliases, locationCount)
            {
                Objectives = QuestContextData.Parse(objectives, mapIds, normalizedMapName.Trim(), french, english)
            });
        }

        return result
            .OrderBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<string> DownloadStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null
            || finalUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(finalUri.Host, "json.tarkov.dev", StringComparison.OrdinalIgnoreCase)
            || finalUri.Port != 443
            || !string.Equals(finalUri.AbsolutePath, uri.AbsolutePath, StringComparison.Ordinal))
        {
            throw new HttpRequestException("La réponse de quêtes ne provient pas de json.tarkov.dev.");
        }

        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new HttpRequestException("La réponse de quêtes dépasse la taille permise.");
        }

        await response.Content
            .LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HashSet<string> FindMapIds(JsonElement root, string normalizedMapName)
    {
        if (!TryGetObjectProperty(root, "data", out var data)
            || !data.TryGetProperty("maps", out var maps))
        {
            throw new JsonException("La réponse tarkov.dev ne contient pas data.maps.");
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in EnumerateObjects(maps))
        {
            if (!TryGetRequiredString(map, "id", out var id)
                || !TryGetRequiredString(map, "normalizedName", out var candidateName)
                || !string.Equals(candidateName, normalizedMapName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(id);
        }
        return result;
    }

    private static int CountLocationsOnMap(
        JsonElement objectives,
        IReadOnlySet<string> mapIds)
    {
        var count = 0;
        foreach (var objective in objectives.EnumerateArray())
        {
            if (objective.ValueKind is not JsonValueKind.Object)
            {
                continue;
            }

            if (objective.TryGetProperty("zones", out var zones)
                && zones.ValueKind is JsonValueKind.Array)
            {
                foreach (var zone in zones.EnumerateArray())
                {
                    if (TryGetRequiredString(zone, "map", out var mapId)
                        && mapIds.Contains(mapId)
                        && TryGetObjectProperty(zone, "position", out var position)
                        && HasFiniteMapPosition(position))
                    {
                        count++;
                    }
                }
            }

            if (!objective.TryGetProperty("possibleLocations", out var possibleLocations)
                || possibleLocations.ValueKind is not JsonValueKind.Array)
            {
                continue;
            }

            foreach (var location in possibleLocations.EnumerateArray())
            {
                if (!TryGetRequiredString(location, "map", out var mapId)
                    || !mapIds.Contains(mapId)
                    || !location.TryGetProperty("positions", out var positions)
                    || positions.ValueKind is not JsonValueKind.Array)
                {
                    continue;
                }

                count += positions.EnumerateArray().Count(HasFiniteMapPosition);
            }
        }
        return count;
    }

    private static bool HasFiniteMapPosition(JsonElement position)
    {
        return TryGetFiniteDouble(position, "x", out _)
            && TryGetFiniteDouble(position, "z", out _);
    }

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object)
                {
                    yield return property.Value;
                }
            }
        }
        else if (value.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object)
                {
                    yield return item;
                }
            }
        }
        else
        {
            throw new JsonException("Le catalogue attendu doit être un objet ou un tableau JSON.");
        }
    }

    private static Dictionary<string, string> ParseTranslations(JsonElement root)
    {
        if (!TryGetObjectProperty(root, "data", out var data))
        {
            throw new JsonException("La réponse de traduction tarkov.dev ne contient pas data.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in data.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                result[property.Name] = property.Value.GetString()!;
            }
        }
        return result;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value)).Trim();
    }

    private static bool TryGetObjectProperty(
        JsonElement parent,
        string propertyName,
        out JsonElement value)
    {
        value = default;
        return parent.ValueKind is JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out value)
            && value.ValueKind is JsonValueKind.Object;
    }

    private static bool TryGetRequiredString(
        JsonElement parent,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (parent.ValueKind is not JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static string? TryGetOptionalString(JsonElement parent, string propertyName)
    {
        return TryGetRequiredString(parent, propertyName, out var value) ? value : null;
    }

    private static bool TryGetFiniteDouble(
        JsonElement parent,
        string propertyName,
        out double value)
    {
        value = default;
        return parent.ValueKind is JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.Number
            && property.TryGetDouble(out value)
            && double.IsFinite(value);
    }
}
