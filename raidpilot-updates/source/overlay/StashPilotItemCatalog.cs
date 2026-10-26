using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RaidPilot.Overlay;

/// <summary>
/// Read-only Tarkov item-name catalogue. Exact unique matches only:
/// ambiguous short labels never become confirmed item identities.
/// The remote catalogue is optional; scanner works with its last local cache.
/// </summary>
internal static class StashPilotItemCatalog
{
    private const string Endpoint = "https://api.tarkov.dev/graphql";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly string CacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidPilot", "stashpilot-items.json");
    private static readonly Regex Spacing = new(@"\s+", RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    internal sealed record Item(string Id, string Name, string ShortName, string IconLink);
    internal sealed record Match(string Name, bool Ambiguous, bool Known);

    internal static async Task<IReadOnlyList<Item>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.PostAsJsonAsync(Endpoint,
                new { query = "query { items { id name shortName iconLink } }" },
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 12_000_000)
                throw new InvalidDataException("Catalogue too large");
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (json.Length is < 10 or > 12_000_000)
                throw new InvalidDataException("Invalid catalogue size");
            var items = Parse(json);
            if (items.Count < 100)
                throw new InvalidDataException("Incomplete catalogue");
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            var temp = CacheFile + ".tmp";
            await File.WriteAllTextAsync(temp, json, cancellationToken);
            File.Move(temp, CacheFile, true);
            return items;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or IOException or UnauthorizedAccessException or InvalidDataException
            or JsonException)
        {
            try
            {
                var file = new FileInfo(CacheFile);
                if (file.Exists && file.Length is > 0 and < 12_000_000)
                    return Parse(await File.ReadAllTextAsync(CacheFile, cancellationToken));
            }
            catch (Exception cached) when (cached is IOException
                or UnauthorizedAccessException or JsonException) { }
            return [];
        }
    }

    internal static IReadOnlyList<Item> Parse(string json)
    {
        using var root = JsonDocument.Parse(json);
        if (!root.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("items", out var list)
            || list.ValueKind != JsonValueKind.Array)
            return [];
        var items = new List<Item>();
        foreach (var entry in list.EnumerateArray())
        {
            string Read(string key) => entry.TryGetProperty(key, out var property)
                && property.ValueKind == JsonValueKind.String
                    ? property.GetString() ?? "" : "";
            var id = Read("id");
            var name = Read("name");
            if (id.Length == 0 || name.Length == 0) continue;
            items.Add(new Item(id, name, Read("shortName"), Read("iconLink")));
        }
        return items;
    }

    internal static Match Resolve(string text, IReadOnlyList<Item> items)
    {
        var token = Normalize(text);
        if (token.Length < 2 || items.Count == 0) return new(text, false, false);
        var matches = items.Where(i => Normalize(i.Name) == token
            || Normalize(i.ShortName) == token).DistinctBy(i => i.Id).Take(2).ToArray();
        if (matches.Length == 1) return new(matches[0].Name, false, true);
        return new(text, matches.Length > 1, false);
    }

    private static string Normalize(string text) =>
        Spacing.Replace(text.Trim(), " ").ToUpperInvariant();
}
