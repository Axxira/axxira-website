namespace RaidPilot.Overlay.Tests;

internal static class MapZoomPreferenceTests
{
    internal const int TestCount = 7;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("zoom global: message de la carte locale accepté", () =>
            Require(Accepts("https://raidpilot-map.local/index.html?map=customs", 2.4)),
            failures);
        Run("zoom global: site en ligne accepté", () =>
            Require(Accepts("https://tarkov.dev/map/woods", 1.8)), failures);
        Run("zoom global: affichage offline historique accepté", () =>
            Require(Accepts("https://raidpilot.local/OfflineMap.html?map=woods", 3)), failures);
        Run("zoom global: sites extérieurs rejetés", () =>
            Require(!Accepts("https://example.com/map/customs", 2)
                && !Accepts("https://tarkov.dev/other", 2)
                && !Accepts("http://raidpilot-map.local/index.html", 2)), failures);
        Run("zoom global: valeurs hors bornes rejetées", () =>
            Require(!Accepts("https://raidpilot-map.local/index.html", 0)
                && !Accepts("https://raidpilot-map.local/index.html", 33)), failures);
        Run("zoom global: messages non pertinents ignorés", () =>
        {
            Require(!OverlayForm.TryParseMapZoomPreference(
                "https://raidpilot-map.local/index.html",
                "{\"type\":\"raidpilot-local-ready\",\"payload\":{\"factor\":2}}", out _));
            Require(!OverlayForm.TryParseMapZoomPreference(
                "https://raidpilot-map.local/index.html",
                "not-json", out _));
        }, failures);
        Run("zoom global: chaîne ou NaN interdit", () =>
        {
            Require(!OverlayForm.TryParseMapZoomPreference(
                "https://raidpilot-map.local/index.html",
                "{\"type\":\"raidpilot-map-zoom-preference\",\"payload\":{\"factor\":\"2\"}}",out _));
        }, failures);
    }

    private static bool Accepts(string uri, double factor) =>
        OverlayForm.TryParseMapZoomPreference(uri,
            System.Text.Json.JsonSerializer.Serialize(new {
                type = "raidpilot-map-zoom-preference",
                payload = new { factor }
            }), out var read) && read == factor;

    private static void Run(string title, Action action, ICollection<string> failures)
    {
        try { action(); Console.WriteLine("OK  " + title); }
        catch (Exception error) { failures.Add(title + " — " + error.Message); }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion de zoom non validée");
    }
}
