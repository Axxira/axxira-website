namespace RaidPilot.Overlay.Tests;

internal static class WebNavigationPolicyTests
{
    internal const int TestCount = 6;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("navigation accepte seulement une carte tarkov.dev HTTPS", AcceptsOnlyTarkovMapUris, failures);
        Run("navigation accepte le renderer offline RaidPilot", AcceptsRaidPilotOfflineMap, failures);
        Run("completion active acceptée", AcceptsMatchingActiveCompletion, failures);
        Run("completion annulée ou étrangère ignorée", RejectsUnknownCompletion, failures);
        Run("completion précédente ignorée pendant un départ", RejectsCompletionWhileStartPending, failures);
        Run("continuation async invalidée par une nouvelle navigation", RejectsStaleAsyncContinuation, failures);
    }

    private static void AcceptsOnlyTarkovMapUris()
    {
        Require(WebNavigationPolicy.TryGetAllowedMapUri(
                "https://tarkov.dev/map/customs?connection=ABC#live",
                out var accepted)
            && accepted is not null
            && accepted.AbsolutePath == "/map/customs",
            "une carte tarkov.dev valide a été refusée");

        foreach (var rejected in new[]
        {
            "http://tarkov.dev/map/customs",
            "https://example.com/map/customs",
            "https://tarkov.dev:444/map/customs",
            "https://tarkov.dev/items",
            "not-a-uri",
        })
        {
            Require(!WebNavigationPolicy.TryGetAllowedMapUri(rejected, out _),
                $"navigation non autorisée acceptée : {rejected}");
        }
    }

    private static void AcceptsRaidPilotOfflineMap()
    {
        Require(
            WebNavigationPolicy.TryGetAllowedMapUri(
                "https://raidpilot.local/OfflineMap.html?map=customs",
                out var accepted)
            && accepted is not null
            && accepted.Host == "raidpilot.local",
            "la carte offline RaidPilot a été refusée");

        Require(
            !WebNavigationPolicy.TryGetAllowedMapUri(
                "https://raidpilot.local/InjectedBehavior.js",
                out _),
            "un asset local arbitraire ne doit pas devenir une navigation principale");
    }

    private static void AcceptsMatchingActiveCompletion()
    {
        Require(WebNavigationPolicy.IsActiveCompletion(42, 42, navigationStartPending: false),
            "la completion active n'a pas été reconnue");
    }

    private static void RejectsUnknownCompletion()
    {
        Require(!WebNavigationPolicy.IsActiveCompletion(42, null, navigationStartPending: false),
            "une completion annulée sans navigation active a été acceptée");
        Require(!WebNavigationPolicy.IsActiveCompletion(42, 41, navigationStartPending: false),
            "une completion étrangère a remplacé la navigation active");
    }

    private static void RejectsCompletionWhileStartPending()
    {
        Require(!WebNavigationPolicy.IsActiveCompletion(42, 42, navigationStartPending: true),
            "l'ancienne completion a gagné pendant le départ suivant");
    }

    private static void RejectsStaleAsyncContinuation()
    {
        Require(WebNavigationPolicy.CanContinueAfterAwait(7, 7, navigationInProgress: false),
            "la continuation courante a été refusée");
        Require(!WebNavigationPolicy.CanContinueAfterAwait(7, 8, navigationInProgress: false),
            "une continuation d'une ancienne page a été acceptée");
        Require(!WebNavigationPolicy.CanContinueAfterAwait(7, 7, navigationInProgress: true),
            "une continuation a couru pendant le chargement suivant");
    }

    private static void Run(string name, Action test, ICollection<string> failures)
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
