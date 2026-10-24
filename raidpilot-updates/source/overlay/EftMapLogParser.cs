using System.Text;
using System.Text.RegularExpressions;

namespace RaidPilot.Overlay;

/// <summary>A map identifier understood by tarkov.dev. It never contains raw log data.</summary>
public sealed record EftMapRoute(string NormalizedName, string Route)
{
    public Uri Url => new(new Uri("https://tarkov.dev", UriKind.Absolute), Route);
}

/// <summary>Extracts only the current map from an EFT application log line.</summary>
public static partial class EftMapLogParser
{
    private static readonly IReadOnlyDictionary<string, EftMapRoute> MapAliases = BuildAliases();

    internal static string? DetectSessionMode(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > 8192) return null;
        var at = line.IndexOf("Session mode:", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var suffix = line[(at + "Session mode:".Length)..].TrimStart();
        var token = suffix.Split([' ', ',', '|', ';', '\r', '\n'], 2,
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token?.ToLowerInvariant() switch
        {
            "pve" => "pve",
            "pvp" => "regular",
            "pvpseason" => "pvp-season",
            _ => null
        };
    }

    [GeneratedRegex(
        @"scene\s+preset\s+path\s*:\s*maps[\\/](?<map>[a-z0-9_\-]+)\.bundle(?=\s|[,;|]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScenePresetPattern();

    [GeneratedRegex(
        @"TRACE-NetworkGameCreate\b[^\r\n]*?\bLocation\s*:\s*(?<map>[a-z0-9_\- ]+?)(?=\s*[,|]|\s*$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NetworkGameCreatePattern();

    /// <summary>
    /// Parses one transient log line. The returned value contains only a public map name and route.
    /// </summary>
    public static bool TryParseLine(string? line, out EftMapRoute? map)
    {
        map = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var match = ScenePresetPattern().Match(line);
        if (!match.Success)
        {
            match = NetworkGameCreatePattern().Match(line);
        }

        return match.Success && TryNormalizeMapToken(match.Groups["map"].Value, out map);
    }

    /// <summary>
    /// Converts a scene bundle name, EFT location id, or known display alias to a stable tarkov.dev route.
    /// This method is intentionally public and deterministic so it can be unit-tested without log files.
    /// </summary>
    public static bool TryNormalizeMapToken(string? token, out EftMapRoute? map)
    {
        map = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var key = NormalizeKey(token);
        return key.Length > 0 && MapAliases.TryGetValue(key, out map);
    }

    private static string NormalizeKey(string token)
    {
        var value = token.Trim().Trim('"', '\'');
        var slash = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
        if (slash >= 0 && slash + 1 < value.Length)
        {
            value = value[(slash + 1)..];
        }

        if (value.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^".bundle".Length];
        }

        var normalized = new StringBuilder(value.Length);
        var previousWasSeparator = false;
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && normalized.Length > 0)
            {
                normalized.Append('_');
                previousWasSeparator = true;
            }
        }

        return normalized.ToString().TrimEnd('_');
    }

    private static IReadOnlyDictionary<string, EftMapRoute> BuildAliases()
    {
        var aliases = new Dictionary<string, EftMapRoute>(StringComparer.OrdinalIgnoreCase);

        Add(aliases, "customs", "customs", "bigmap", "bigmap_preset", "customs", "customs_preset",
            "customs-2d", "customs-3d", "customs-3d-dorms");
        Add(aliases, "factory", "factory", "factory", "factory4_day", "factory_day",
            "factory4_day_preset", "factory_day_preset", "factory-2d", "factory-3d");
        Add(aliases, "night-factory", "night-factory", "factory4_night", "factory_night", "night_factory",
            "factory4_night_preset", "factory_night_preset");
        Add(aliases, "ground-zero", "ground-zero", "sandbox", "sandbox_preset",
            "ground_zero", "groundzero", "ground-zero-2d", "ground-zero-3d",
            "sandbox_start", "sandbox_start_preset", "ground_zero_tutorial");
        Add(aliases, "ground-zero-21", "ground-zero-21", "sandbox_high", "sandbox_high_preset", "ground_zero_21");
        Add(aliases, "streets-of-tarkov", "streets-of-tarkov", "tarkovstreets", "city", "city_preset", "streets",
            "streets_of_tarkov", "streets_of_tarkov_preset", "streets-2d", "streets-3d", "streets-3d-caches", "streets-3d-lexos");
        Add(aliases, "interchange", "interchange", "interchange", "interchange_preset", "shopping_mall", "shopping_mall_preset", "interchange-2d", "interchange-3d");
        Add(aliases, "the-lab", "the-lab", "laboratory", "laboratory_preset", "lab", "labs", "lab_preset", "labs_preset", "labs-2d");
        Add(aliases, "the-lab-dark", "the-lab-dark", "dark_lab", "darklab", "lab_dark", "laboratory_dark",
            "laboratory_dark_preset", "dark_lab_preset");
        Add(aliases, "the-labyrinth", "the-labyrinth", "labyrinth", "labyrinth_preset", "labrys", "labrys_preset", "labyrinth-2d");
        Add(aliases, "lighthouse", "lighthouse", "lighthouse", "lighthouse_preset", "lighthouse-2d", "lighthouse-2d-landscape", "lighthouse-3d");
        Add(aliases, "reserve", "reserve", "rezervbase", "rezerv_base", "rezerv_base_preset", "reserve", "reserve_preset", "reserve-2d", "reserve-3d", "reserve-3d-tunnels");
        Add(aliases, "shoreline", "shoreline", "shoreline", "shoreline_preset", "shoreline-2d", "shoreline-3d", "shoreline-3d-resort");
        Add(aliases, "woods", "woods", "woods", "woods_preset", "woods-2d", "woods-3d");
        Add(aliases, "terminal", "terminal", "terminal", "terminal_preset", "terminal-2d");
        Add(aliases, "icebreaker", "icebreaker", "icebreaker", "icebreaker_preset", "ice_breaker", "ice_breaker_preset", "boreas");

        return aliases;
    }

    private static void Add(
        Dictionary<string, EftMapRoute> aliases,
        string normalizedName,
        string routeSlug,
        params string[] keys)
    {
        var map = new EftMapRoute(normalizedName, $"/map/{routeSlug}");
        aliases[NormalizeKey(normalizedName)] = map;
        aliases[NormalizeKey(routeSlug)] = map;
        foreach (var key in keys)
        {
            aliases[NormalizeKey(key)] = map;
        }
    }
}
