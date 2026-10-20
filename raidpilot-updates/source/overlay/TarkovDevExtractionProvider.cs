using System.Net.Http;
using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Charge et transforme uniquement les donnees d'extraction publiees par
/// json.tarkov.dev. Le parseur statique reste independant du reseau afin de
/// pouvoir etre teste avec un instantane JSON local.
/// </summary>
public sealed class TarkovDevExtractionProvider
{
    // Le catalogue officiel a dépassé 8 Mio en octobre 2026. Une borne de
    // 16 Mio garde le téléchargement limité tout en laissant de la marge aux
    // nouvelles cartes.
    private const long MaximumResponseBytes = 16L * 1024L * 1024L;
    public static readonly Uri MapsUri = new("https://json.tarkov.dev/regular/maps");
    public static readonly Uri FrenchTranslationsUri = new("https://json.tarkov.dev/regular/maps_fr");
    public static readonly Uri EnglishTranslationsUri = new("https://json.tarkov.dev/regular/maps_en");

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    // Fail closed: tarkov.dev exposes the extraction geometry but does not
    // reliably attach every in-raid requirement to the corresponding exit.
    // Only exits audited as requiring no key, payment, cooperation, flare,
    // switch, equipment restriction or timing window may remain "None".
    // Exact map scoping prevents a generic label reused elsewhere from being
    // trusted accidentally. Each entry accepts the current stable data id and
    // the raw game/name key so a translation change alone does not break it.
    private static readonly IReadOnlyDictionary<string, HashSet<string>>
        OrdinaryExitIdentities = CreateOrdinaryExitAllowList();

    private readonly HttpClient _httpClient;
    private readonly TarkovDevJsonCache? _cache;

    public TarkovDevExtractionProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _cache = httpClient is null ? new TarkovDevJsonCache() : null;
    }

    internal TarkovDevExtractionProvider(HttpClient httpClient, TarkovDevJsonCache? cache)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache;
    }

    /// <summary>
    /// Telecharge la carte reguliere et ses traductions francaises, puis
    /// retourne les extractions de <paramref name="normalizedMapName"/>.
    /// </summary>
    public async Task<IReadOnlyList<ExtractionCandidate>> LoadAsync(
        string normalizedMapName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedMapName))
        {
            throw new ArgumentException("Le nom normalise de la carte est requis.", nameof(normalizedMapName));
        }

        var mapsTask = LoadDocumentAsync(
            "regular-maps",
            MapsUri,
            "maps",
            cancellationToken);
        var translationsTask = LoadDocumentAsync(
            "regular-maps-fr",
            FrenchTranslationsUri,
            requiredDataProperty: null,
            cancellationToken);
        var englishTranslationsTask = LoadDocumentAsync(
            "regular-maps-en",
            EnglishTranslationsUri,
            requiredDataProperty: null,
            cancellationToken);
        await Task.WhenAll(mapsTask, translationsTask, englishTranslationsTask).ConfigureAwait(false);

        return Parse(
            await mapsTask.ConfigureAwait(false),
            await translationsTask.ConfigureAwait(false),
            normalizedMapName,
            await englishTranslationsTask.ConfigureAwait(false));
    }

    private Task<string> LoadDocumentAsync(
        string cacheKey,
        Uri uri,
        string? requiredDataProperty,
        CancellationToken cancellationToken)
    {
        return _cache is null
            ? DownloadStringAsync(uri, cancellationToken)
            : _cache.GetOrFetchAsync(
                cacheKey,
                MaximumResponseBytes,
                requiredDataProperty,
                token => DownloadStringAsync(uri, token),
                cancellationToken);
    }

    /// <summary>
    /// Nom explicite utilise par le consommateur de l'overlay. Il delegue au
    /// meme chargement et conserve <see cref="LoadAsync"/> pour les appels
    /// generiques et les tests unitaires.
    /// </summary>
    public Task<IReadOnlyList<ExtractionCandidate>> GetForMapAsync(
        string normalizedMapName,
        CancellationToken cancellationToken = default)
    {
        return LoadAsync(normalizedMapName, cancellationToken);
    }

    /// <summary>
    /// Parse les deux reponses JSON de tarkov.dev sans effectuer d'acces
    /// reseau. Les entrees incompletes sont ignorees plutot que transformees
    /// en positions trompeuses.
    /// </summary>
    public static IReadOnlyList<ExtractionCandidate> Parse(
        string mapsJson,
        string frenchTranslationsJson,
        string normalizedMapName,
        string? englishTranslationsJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(frenchTranslationsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedMapName);

        using var mapsDocument = JsonDocument.Parse(mapsJson);
        using var translationsDocument = JsonDocument.Parse(frenchTranslationsJson);
        using var englishTranslationsDocument = string.IsNullOrWhiteSpace(englishTranslationsJson)
            ? null
            : JsonDocument.Parse(englishTranslationsJson);

        var translations = ParseTranslations(translationsDocument.RootElement);
        var englishTranslations = englishTranslationsDocument is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ParseTranslations(englishTranslationsDocument.RootElement);
        var map = FindMap(mapsDocument.RootElement, normalizedMapName.Trim());
        if (map is null)
        {
            throw new KeyNotFoundException(
                $"La carte tarkov.dev '{normalizedMapName}' est introuvable.");
        }

        if (!map.Value.TryGetProperty("extracts", out var extracts)
            || extracts.ValueKind is not JsonValueKind.Array)
        {
            return Array.Empty<ExtractionCandidate>();
        }

        var result = new List<ExtractionCandidate>();
        foreach (var extract in extracts.EnumerateArray())
        {
            if (TryParseExtraction(
                extract,
                normalizedMapName,
                translations,
                englishTranslations,
                out var candidate))
            {
                result.Add(candidate);
            }
        }

        return result;
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
            throw new HttpRequestException("La réponse d'extraction ne provient pas de json.tarkov.dev.");
        }

        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new HttpRequestException("La réponse d'extraction dépasse la taille permise.");
        }

        await response.Content
            .LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken)
            .ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement? FindMap(JsonElement root, string normalizedMapName)
    {
        if (!TryGetObjectProperty(root, "data", out var data)
            || !data.TryGetProperty("maps", out var maps))
        {
            throw new JsonException("La reponse tarkov.dev ne contient pas data.maps.");
        }

        if (maps.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in maps.EnumerateObject())
            {
                if (HasNormalizedName(property.Value, normalizedMapName))
                {
                    return property.Value;
                }
            }
        }
        else if (maps.ValueKind is JsonValueKind.Array)
        {
            // Accepte aussi les instantanes historiques deja aplatis.
            foreach (var map in maps.EnumerateArray())
            {
                if (HasNormalizedName(map, normalizedMapName))
                {
                    return map;
                }
            }
        }
        else
        {
            throw new JsonException("data.maps doit etre un objet ou un tableau JSON.");
        }

        return null;
    }

    private static bool HasNormalizedName(JsonElement map, string expected)
    {
        return map.ValueKind is JsonValueKind.Object
            && map.TryGetProperty("normalizedName", out var normalizedName)
            && normalizedName.ValueKind is JsonValueKind.String
            && string.Equals(normalizedName.GetString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseTranslations(JsonElement root)
    {
        if (!TryGetObjectProperty(root, "data", out var data))
        {
            throw new JsonException("La reponse de traduction tarkov.dev ne contient pas data.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in data.EnumerateObject())
        {
            if (property.Value.ValueKind is not JsonValueKind.String)
            {
                continue;
            }

            var value = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                result[property.Name] = value;
            }
        }

        return result;
    }

    private static bool TryParseExtraction(
        JsonElement extract,
        string normalizedMapName,
        IReadOnlyDictionary<string, string> translations,
        IReadOnlyDictionary<string, string> englishTranslations,
        out ExtractionCandidate candidate)
    {
        candidate = null!;
        if (extract.ValueKind is not JsonValueKind.Object
            || !TryGetRequiredString(extract, "id", out var id)
            || !TryGetRequiredString(extract, "name", out var nameKey)
            || !TryGetObjectProperty(extract, "position", out var position)
            || !TryGetFiniteDouble(position, "x", out var x)
            || !TryGetFiniteDouble(position, "y", out var y)
            || !TryGetFiniteDouble(position, "z", out var z))
        {
            return false;
        }

        var name = translations.TryGetValue(nameKey, out var translatedName)
            ? translatedName
            : nameKey;
        var aliases = new[]
            {
                name,
                englishTranslations.TryGetValue(nameKey, out var englishName) ? englishName : nameKey,
                nameKey,
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var faction = TryGetOptionalString(extract, "faction");
        var requirements = ExtractionRequirement.None;

        if (HasTransferRequirement(extract))
        {
            requirements |= ExtractionRequirement.Transfer;
        }

        // Le flux public recopie actuellement les interrupteurs d'une carte
        // sur toutes ses extractions. Ce champ ne permet donc pas d'affirmer
        // qu'une sortie précise exige l'interrupteur.
        if (IsCooperativeExit(nameKey, name, englishTranslations))
        {
            requirements |= ExtractionRequirement.Cooperation;
        }

        if (requirements == ExtractionRequirement.None
            && !IsAllowListedOrdinaryExit(
                normalizedMapName,
                id,
                nameKey))
        {
            requirements |= ExtractionRequirement.Unknown;
        }

        candidate = new ExtractionCandidate(id, name, faction, x, y, z, requirements, aliases);
        return true;
    }

    private static bool IsAllowListedOrdinaryExit(
        string normalizedMapName,
        string id,
        string nameKey)
    {
        var mapKey = normalizedMapName.Trim();
        if (!OrdinaryExitIdentities.TryGetValue(mapKey, out var identities))
        {
            return false;
        }

        return new[] { id, nameKey }
            .Select(ExtractionAdvisor.NormalizeDisplayedName)
            .Any(identities.Contains);
    }

    private static IReadOnlyDictionary<string, HashSet<string>>
        CreateOrdinaryExitAllowList()
    {
        static HashSet<string> Identities(params string[] values)
        {
            return values
                .Select(ExtractionAdvisor.NormalizeDisplayedName)
                .Where(value => value.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
        }

        return new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["factory"] = Identities(
                "f16ce849a9d153a8813b6bf6a049d530f1cc44c2", "Gate 3",
                "29b887a73725274d657c8a5366614b9b900387e2", "Camera Bunker Door",
                "1733510cd360e7a13af356612afad7214aae64e4", "Office Window"),
            ["night-factory"] = Identities(
                "51ab65d13d6e1b2ffcd590d1fee5bef867f1738d", "Gate 3",
                "c3c1ddd12a9034eb3f5db817c25f0b20217760fa", "Camera Bunker Door",
                "f643c220960ee838cb19cccb0ecce4288af427cf", "Office Window"),
            ["customs"] = Identities(
                "ab12c1f902a669fb0c940820e8cc40d7131e3208", "ex_customs_pmc_crossroads",
                "aa56e9228b8452533873eeffa11b587007f49429", "RUAF Roadblock",
                "59c2c12e375a1b45a8e5ac1a32c5581b593aa4df", "ex_customs_pmc_zb1011",
                "f4cdf3dbe54ef647a2a798266a39f1761f871b96", "ex_customs_pmc_trailerpark",
                "f367bab752ed8474989104613d1151e9434f2bd4", "ex_customs_pmc_rail_milbase",
                "92e7412193276c1044299b51d930440cdab835e4", "ex_customs_scav_admin",
                "1e7baccc265b49195aead2e783779cbca83d73f3", "Old Road Gate",
                "ad8fc418508df45199927afc5ea8c981337ecd64", "RUAF Roadblock_scav",
                "6eee3e566de0b079cd76f204d297ba954b301e8c", "Sniper Roadblock",
                "c9e36de4f1b9f73f4698b5ba46135a1e0aafb434", "Factory Shacks",
                "fc4876c616edf234b1b0b75fb02fce784e54a6c2", "ex_customs_scav_crossroads",
                "f865b04a5d4c6b10d92f8783b51b0b486f55cf79", "Warehouse 4",
                "74840d2d5d9c332e466148e94be1ab4ef0fb3ac4", "ex_customs_scav_rail_milbase",
                "bf421e259c2e851b159401432318bcca326b16d5", "ex_customs_scav_rail_port",
                "0eedf7dfd9db21b60e216ebccbf52271a06cddf6", "ex_customs_scav_medgate",
                "46979e6dd937e83470b013382282919388a07f8c", "ex_customs_scav_rail_tarkov",
                "973dfce3d353b26fe9b3e9373bd4536d26fffc05", "ex_customs_scav_milbase_cp",
                "500734adcba6b80cc42b3ca3f4884f89aceffca0", "Beyond Fuel Tank",
                "f57705238de721e427d8ea262f7226f804e88726", "ex_customs_scav_zb1011",
                "f3f0171d1c8396a152e319f372c4e9d9df120e73", "ex_customs_scav_trailerpark"),
            ["woods"] = Identities(
                "5e2529edb827c041cb4198afb28b8c1ded6eace9", "Outskirts",
                "bd8c86edf705cfb2bd02f8ccc95e64ed3450760c", "UN Roadblock",
                "69c8400dbe1a154ddc5a4617d3ae2053239f4f33", "RUAF Gate",
                "edceeaccf9cb0f6c3fd47c0e5625f4cdd2f49e9e", "un-sec",
                "beefde059ae2d331c8d568066518e0293ff9da59", "Old Station",
                "3e64a0665e093dac84499dc50e2768684e8bd508", "Scav House",
                "6363090af9e0ab1a3d0f7154ea89e1c1c362e30d", "West Border",
                "7692ab53faec4d1c375212a2bca01796de7c4cdb", "Mountain Stash",
                "dc49f6444b7e835b4cfb2589e1df68a21a9bf3c9", "East Gate",
                "b38ddc0bb3e976a7c1aae719eec1fdab83369da7", "RUAF Roadblock",
                "495bcac9e8a677cef40194ea3c57d65f2c4276d9", "Dead Man's Place",
                "b6371d7a90e044c79df5dbc651983ddabbc9b2bf", "The Boat"),
            ["lighthouse"] = Identities(
                "73ce3c0b6f2e55c2d5d38132e4046244cb7ddcfa", "Coastal_South_Road",
                "7abf70359636bf5dc3515359a3553cb0731d4f4e", "Nothern_Checkpoint",
                "65541e3df26023e91dd4f156e6409ef06eb0027a", "Shorl_free",
                "aa9a937fb2c3c2d51fe21ab457ab0613ff5e2539", "Scav_Coastal_South",
                "29fc02598426cdcb78bf9e07e188680ce0e8730d", "Shorl_free_scav",
                "3ab7031964d90e541d16b1307d2017f424518b49", "Scav_Underboat_Hideout",
                "e2d5caeaeb8747a3182be5bfc98cad24892a2d00", "Scav_Hideout_at_the_grotto",
                "4d2faa68a953a89cc697348f947ef24d9a626380", "Scav_Industrial_zone"),
            ["shoreline"] = Identities(
                "cf857a4cc48ed3c8c6dd27f7d55d6348ffbdbc68", "Tunnel",
                "ef41c42e56e8f67e5835245dd4f66c4ed8684db9", "Lighthouse_pass",
                "a302bfd6b8b040202ce873db7386f930318c3f0b", "Road to Customs",
                "04d0f85f6abb2d77d803449be6918c26efc58478", "Road_at_railbridge",
                "3475b9df1bf661d3c746ff1a1a8493bae828b939", "Adm Basement",
                "566161efb75d27f6347b9d0f610cd423dcdf319f", "Lighthouse",
                "9daba46bc5e06708a54c6274678e34e6bf13de12", "Svetliy Dead End",
                "aa2c447aa7f49a1795efd56aad7ef0670888425f", "Wrecked Road",
                "95406652decb52abdd763626e88024931990da7d", "Scav Road to Customs",
                "a048afd75fd279f62ee3da4fb0f38c42d8b7a934", "RWing Gym Entrance",
                "1c7d8b060c38232059ce879b560a5979d12d7239", "Ruined House Fence"),
            ["reserve"] = Identities(
                "493cc117068458f12944b1b83710116395831e21", "Exit2",
                "4603ee6acd4e4873aadc865fc476e0c0e3b4c724", "Exit1",
                "037fb35378864fe476e2b1bb8b6a613ee8ac7101", "Exit4",
                "3a5be2ffaa91dc56af778ea406023197d32b33c7", "Exit3"),
            ["interchange"] = Identities(
                "4cf9e8c499daa3f18291b2fce601e6dd0be66882", "ex_interchange_scav_railway",
                "714a00d27d6ac36ca35a714c54b5517b32373914", "ex_interchange_pmc_railway",
                "56bd754c4ce1864b212e908114bd6190940e01c2", "ex_interchange_pmc_emercom",
                "f60ee7a996094e3a9c0cb1155a6e20558a4386db", "ex_interchange_scav_emercom"),
            ["streets-of-tarkov"] = Identities(
                "51cfcc623bcf29fa75c44fe4997e4b130ffbba34", "E5",
                "62268c2eccf21f2a38b8ad9c7a18fcdd470b6ff3", "E4",
                "faaa78ad8ea79ae073fc497da44fedc3cc0364be", "E3",
                "321358e7c712157a0692f8212c0ce644a85d8d9a", "E1",
                "dd263ba81dc18be1d7e25f213b19481699c0a898", "E7",
                "6ba57e258ce3f2d026ef5aa22e2c8b046e427f2d", "E2",
                "f44cf9cd1192e55035944c19fb2745c9c8c58692", "E6",
                "177bd2a8abcb68ca8d1964d6c5efdfeeb3f6abb8", "scav_e7",
                "b89e9f9cc4529c5ba4b2487c6a5331c694d0d22a", "scav_e2",
                "9ab4ef82011a4ebdb53413d61c806c12d731ebd4", "scav_e8",
                "91ef53b9da2e2c8d3dab3a51536d24d149af44c5", "scav_e5",
                "d40366cc1831e903e3c9ddb3a28063a7baa45a6a", "scav_e3",
                "1adaba531eb66eae1ef23896862a3681ed23a67f", "scav_e4",
                "238a87ecf9488888592df0fc488eb2ccd607d68f", "scav_e6"),
            ["ground-zero"] = Identities(
                "58b21021d80db728cd7db688a00677980593bf85", "Nakatani_stairs_free_exit",
                "495c627fb5f7498fb7621bf193f16b74e1c952b9", "UN Roadblock",
                "af20846a32f03fec90986c60eb732da7b87f72b0", "Unity_free_exit",
                "47e9492ade77358f16b17e94f44e09fa50b82276", "East Gate"),
            ["ground-zero-21"] = Identities(
                "c812e0bac1d630c34af374428e34cf10dc344e9b", "Unity_free_exit",
                "1bb328c35dbf9c2127defe6df5a48c630ece92a1", "Nakatani_stairs_free_exit"),
        };
    }

    private static bool IsCooperativeExit(
        string nameKey,
        string localizedName,
        IReadOnlyDictionary<string, string> englishTranslations)
    {
        var englishName = englishTranslations.TryGetValue(nameKey, out var translated)
            ? translated
            : string.Empty;
        return ContainsCoopMarker(nameKey)
            || ContainsCoopMarker(localizedName)
            || ContainsCoopMarker(englishName);
    }

    private static bool ContainsCoopMarker(string value)
    {
        var compact = new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return compact.Contains("coop", StringComparison.Ordinal)
            || compact.Contains("scavcooperation", StringComparison.Ordinal);
    }

    private static bool HasTransferRequirement(JsonElement extract)
    {
        return extract.TryGetProperty("transferItem", out var transferItem)
            && transferItem.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False);
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
        if (parent.ValueKind is not JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
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
