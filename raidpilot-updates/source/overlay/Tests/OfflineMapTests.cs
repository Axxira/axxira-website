using System.Text.Json;

namespace RaidPilot.Overlay.Tests;

internal static class OfflineMapTests
{
    internal const int TestCount = 6;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("offline map: navigation locale autorisée", AllowsOfflineMapUri, failures);
        Run("offline map: chemin local arbitraire refusé", RejectsArbitraryOfflinePath, failures);
        Run("offline map: renderer expose API Leaflet compatible", RendererExposesMapApi, failures);
        Run("offline map: comportement accepte raidpilot.local", BehaviorAcceptsOfflineOrigin, failures);
        Run("offline map: catalogue calibré contient les maps", ConfigContainsMaps, failures);
        Run("offline map: fallback distant existe pour maps tile-only", TileOnlyMapsHaveFallback, failures);
    }

    private static void AllowsOfflineMapUri()
    {
        Require(
            WebNavigationPolicy.TryGetAllowedMapUri(
                "https://raidpilot.local/OfflineMap.html?map=customs",
                out var uri)
            && uri is not null,
            "l'URL locale RaidPilot devrait être autorisée");
    }

    private static void RejectsArbitraryOfflinePath()
    {
        Require(
            !WebNavigationPolicy.TryGetAllowedMapUri(
                "https://raidpilot.local/anything.html?map=customs",
                out _),
            "un chemin local arbitraire ne doit pas être autorisé");
    }

    private static void RendererExposesMapApi()
    {
        var source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "OfflineMap.js"));
        Require(
            source.Contains("__raidPilotOfflineMap", StringComparison.Ordinal)
            && source.Contains("latLngToContainerPoint", StringComparison.Ordinal)
            && source.Contains("containerPointToLatLng", StringComparison.Ordinal)
            && source.Contains("setView(value", StringComparison.Ordinal)
            && source.Contains("panTo(value", StringComparison.Ordinal),
            "le renderer offline n'expose pas l'API nécessaire");
    }

    private static void BehaviorAcceptsOfflineOrigin()
    {
        var source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js"));
        Require(
            source.Contains("https://raidpilot.local", StringComparison.Ordinal)
            && source.Contains("window.__raidPilotOfflineMap", StringComparison.Ordinal),
            "InjectedBehavior ne reconnaît pas le renderer offline");
    }

    private static void ConfigContainsMaps()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "OfflineMaps",
                    "config.json")));
        Require(
            document.RootElement.ValueKind == JsonValueKind.Array
            && document.RootElement.GetArrayLength() >= 13,
            "le catalogue offline ne contient pas toutes les cartes interactives");
    }

    private static void TileOnlyMapsHaveFallback()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "OfflineMaps",
                    "config.json")));
        var required = new HashSet<string>(
            new[] { "icebreaker", "the-lab", "the-labyrinth" },
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("normalizedName", out var map))
            {
                continue;
            }
            var name = map.GetString() ?? string.Empty;
            if (!required.Contains(name))
            {
                continue;
            }
            Require(
                entry.TryGetProperty("assetUrl", out var url)
                && url.ValueKind == JsonValueKind.String
                && url.GetString()?.StartsWith(
                    "https://raw.githubusercontent.com/",
                    StringComparison.OrdinalIgnoreCase) == true,
                $"la map {name} n'a pas de fallback GitHub");
            required.Remove(name);
        }
        Require(required.Count == 0, "des maps tile-only manquent au catalogue offline");
    }

    private static void Run(
        string name,
        Action test,
        ICollection<string> failures)
    {
        try
        {
            test();
            Console.WriteLine($"OK  {name}");
        }
        catch (Exception exception)
        {
            failures.Add($"{name} — {exception.Message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
