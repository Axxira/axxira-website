using System.Net.Http;
using System.Text.Json;

namespace RaidPilot.Overlay;

internal readonly record struct BoundaryHazardPoint(double X, double Z);

internal sealed record BoundaryHazardZone(
    string Id,
    double X,
    double Y,
    double Z,
    IReadOnlyList<BoundaryHazardPoint> Outline,
    double? Top,
    double? Bottom);

internal readonly record struct BoundaryHazardProximity(
    bool Warning,
    bool Critical,
    double DistanceMeters,
    double EffectiveDistanceMeters,
    bool InsideDangerZone)
{
    internal static BoundaryHazardProximity Safe => new(
        false,
        false,
        double.PositiveInfinity,
        double.PositiveInfinity,
        false);
}

/// <summary>
/// Garde-fou de bord de carte basé sur les zones sniper publiées par tarkov.dev.
/// Les champs de mines restent gérés par TarkovDevMineProvider.
/// </summary>
internal sealed class TarkovDevBoundaryProvider
{
    private const long MaximumResponseBytes = 16L * 1024L * 1024L;

    internal const double WarningDistanceMeters = 30.0;
    internal const double CriticalDistanceMeters = 14.0;
    internal const double WarningReleaseDistanceMeters = 36.0;
    internal const double CriticalReleaseDistanceMeters = 18.0;
    internal const double MaximumUncertaintyExpansionMeters = 6.0;
    internal const double VerticalSafetyMarginMeters = 3.0;

    private static readonly Uri MapsUri =
        new("https://json.tarkov.dev/regular/maps");
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private readonly HttpClient _httpClient;
    private readonly TarkovDevJsonCache? _cache;

    internal TarkovDevBoundaryProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _cache = httpClient is null ? new TarkovDevJsonCache() : null;
    }

    internal TarkovDevBoundaryProvider(
        HttpClient httpClient,
        TarkovDevJsonCache? cache)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache;
    }

    internal async Task<IReadOnlyList<BoundaryHazardZone>> GetForMapAsync(
        string normalizedMapName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedMapName);

        var json = _cache is null
            ? await DownloadStringAsync(cancellationToken).ConfigureAwait(false)
            : await _cache.GetOrFetchAsync(
                "regular-maps",
                MaximumResponseBytes,
                "maps",
                DownloadStringAsync,
                cancellationToken).ConfigureAwait(false);

        return Parse(json, normalizedMapName);
    }

    internal static IReadOnlyList<BoundaryHazardZone> Parse(
        string mapsJson,
        string normalizedMapName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapsJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedMapName);

        using var document = JsonDocument.Parse(mapsJson);
        if (!TryGetObjectProperty(document.RootElement, "data", out var data)
            || !data.TryGetProperty("maps", out var maps))
        {
            throw new JsonException(
                "La réponse tarkov.dev ne contient pas data.maps.");
        }

        var expected = normalizedMapName.Trim();
        foreach (var map in EnumerateObjects(maps))
        {
            if (!TryGetString(map, "normalizedName", out var mapName)
                || !string.Equals(
                    mapName,
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!map.TryGetProperty("hazards", out var hazards)
                || hazards.ValueKind is not JsonValueKind.Array)
            {
                return Array.Empty<BoundaryHazardZone>();
            }

            var result = new List<BoundaryHazardZone>();
            var index = 0;
            foreach (var hazard in hazards.EnumerateArray())
            {
                index++;
                if (hazard.ValueKind is not JsonValueKind.Object
                    || !TryGetString(
                        hazard,
                        "hazardType",
                        out var hazardType)
                    || !string.Equals(
                        hazardType,
                        "sniper",
                        StringComparison.OrdinalIgnoreCase)
                    || !TryGetObjectProperty(
                        hazard,
                        "position",
                        out var position)
                    || !TryGetFinite(position, "x", out var x)
                    || !TryGetFinite(position, "y", out var y)
                    || !TryGetFinite(position, "z", out var z))
                {
                    continue;
                }

                var outline = ParseOutline(hazard);
                var top = TryGetFinite(hazard, "top", out var parsedTop)
                    ? parsedTop
                    : (double?)null;
                var bottom = TryGetFinite(
                    hazard,
                    "bottom",
                    out var parsedBottom)
                    ? parsedBottom
                    : (double?)null;

                if (top.HasValue
                    && bottom.HasValue
                    && top.Value < bottom.Value)
                {
                    (top, bottom) = (bottom, top);
                }

                result.Add(new BoundaryHazardZone(
                    $"{expected}:boundary:{index}",
                    x,
                    y,
                    z,
                    outline,
                    top,
                    bottom));
            }

            return result;
        }

        throw new KeyNotFoundException(
            $"La carte tarkov.dev '{normalizedMapName}' est introuvable.");
    }

    internal static BoundaryHazardProximity EvaluateProximity(
        RealtimePose pose,
        IReadOnlyList<BoundaryHazardZone> zones,
        bool warningAlreadyActive = false,
        bool criticalAlreadyActive = false)
    {
        ArgumentNullException.ThrowIfNull(zones);
        if (!new[]
            {
                pose.X,
                pose.Y,
                pose.Z,
                pose.PositionUncertaintyMeters,
            }.All(double.IsFinite)
            || pose.PositionUncertaintyMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pose));
        }

        var bestDistance = double.PositiveInfinity;
        var inside = false;
        foreach (var zone in zones)
        {
            if (!IsVerticallyRelevant(pose.Y, zone))
            {
                continue;
            }

            var distance = HorizontalDistanceToZone(
                pose.X,
                pose.Z,
                zone,
                out var insideThisZone);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                inside = insideThisZone;
            }
        }

        if (!double.IsFinite(bestDistance))
        {
            return BoundaryHazardProximity.Safe;
        }

        var uncertaintyExpansion = Math.Min(
            pose.PositionUncertaintyMeters,
            MaximumUncertaintyExpansionMeters);
        var effectiveDistance = Math.Max(
            0.0,
            bestDistance - uncertaintyExpansion);

        var warningThreshold = warningAlreadyActive
            ? WarningReleaseDistanceMeters
            : WarningDistanceMeters;
        var criticalThreshold = criticalAlreadyActive
            ? CriticalReleaseDistanceMeters
            : CriticalDistanceMeters;

        return new BoundaryHazardProximity(
            inside || effectiveDistance <= warningThreshold,
            inside || effectiveDistance <= criticalThreshold,
            bestDistance,
            effectiveDistance,
            inside);
    }

    private async Task<string> DownloadStringAsync(
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(
                MapsUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength
            is > MaximumResponseBytes)
        {
            throw new HttpRequestException(
                "La réponse des limites dépasse la taille permise.");
        }

        await response.Content
            .LoadIntoBufferAsync(
                MaximumResponseBytes,
                cancellationToken)
            .ConfigureAwait(false);
        return await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<BoundaryHazardPoint> ParseOutline(
        JsonElement hazard)
    {
        if (!hazard.TryGetProperty("outline", out var outline)
            || outline.ValueKind is not JsonValueKind.Array)
        {
            return Array.Empty<BoundaryHazardPoint>();
        }

        var points = new List<BoundaryHazardPoint>();
        foreach (var point in outline.EnumerateArray())
        {
            if (TryGetFinite(point, "x", out var x)
                && TryGetFinite(point, "z", out var z))
            {
                points.Add(new BoundaryHazardPoint(x, z));
            }
        }

        return points.Count >= 3
            ? points.ToArray()
            : Array.Empty<BoundaryHazardPoint>();
    }

    private static bool IsVerticallyRelevant(
        double y,
        BoundaryHazardZone zone)
    {
        if (zone.Top is not { } top
            || zone.Bottom is not { } bottom)
        {
            return true;
        }

        return y >= bottom - VerticalSafetyMarginMeters
            && y <= top + VerticalSafetyMarginMeters;
    }

    private static double HorizontalDistanceToZone(
        double x,
        double z,
        BoundaryHazardZone zone,
        out bool inside)
    {
        var polygon = zone.Outline;
        if (polygon.Count < 3)
        {
            inside = false;
            return Math.Sqrt(
                Math.Pow(x - zone.X, 2)
                + Math.Pow(z - zone.Z, 2));
        }

        inside = PointInPolygon(x, z, polygon);
        if (inside)
        {
            return 0.0;
        }

        var minimum = double.PositiveInfinity;
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            minimum = Math.Min(
                minimum,
                DistanceToSegment(
                    x,
                    z,
                    a.X,
                    a.Z,
                    b.X,
                    b.Z));
        }
        return minimum;
    }

    private static bool PointInPolygon(
        double x,
        double z,
        IReadOnlyList<BoundaryHazardPoint> polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1;
             i < polygon.Count;
             j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            var intersects =
                ((a.Z > z) != (b.Z > z))
                && x < ((b.X - a.X) * (z - a.Z)
                        / ((b.Z - a.Z) + double.Epsilon))
                    + a.X;
            if (intersects)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static double DistanceToSegment(
        double px,
        double pz,
        double ax,
        double az,
        double bx,
        double bz)
    {
        var dx = bx - ax;
        var dz = bz - az;
        var lengthSquared = (dx * dx) + (dz * dz);
        if (lengthSquared <= double.Epsilon)
        {
            return Math.Sqrt(
                Math.Pow(px - ax, 2)
                + Math.Pow(pz - az, 2));
        }

        var t = Math.Clamp(
            (((px - ax) * dx) + ((pz - az) * dz))
            / lengthSquared,
            0.0,
            1.0);
        var closestX = ax + (t * dx);
        var closestZ = az + (t * dz);
        return Math.Sqrt(
            Math.Pow(px - closestX, 2)
            + Math.Pow(pz - closestZ, 2));
    }

    private static IEnumerable<JsonElement> EnumerateObjects(
        JsonElement value)
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
            throw new JsonException(
                "Le catalogue des cartes doit être un objet ou tableau.");
        }
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

    private static bool TryGetString(
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

    private static bool TryGetFinite(
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