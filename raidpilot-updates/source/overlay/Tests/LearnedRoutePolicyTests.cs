namespace RaidPilot.Overlay.Tests;

internal static class LearnedRoutePolicyTests
{
    internal const int TestCount = 8;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("route fiable accepte des raccords parcourus", AcceptsTrustedPlan, failures);
        Run("route refuse un raccord final de huit mètres", RejectsLongTargetConnector, failures);
        Run("route refuse un raccord initial de cinq mètres", RejectsLongStartConnector, failures);
        Run("route suspendue dès quatre mètres de dérive dépassés", RejectsUncertainPose, failures);
        Run("progression de route reste monotone", ProgressNeverMovesBackward, failures);
        Run("progression ne coupe pas un virage", ProgressDoesNotCutCorner, failures);
        Run("progression ne saute pas un autre étage", ProgressDoesNotSkipFloor, failures);
        Run("cache ne dépasse jamais le raccord fiable", CacheCannotOutgrowTrustedConnector, failures);
    }

    private static void AcceptsTrustedPlan()
    {
        Require(LearnedRoutePolicy.IsTrusted(Plan(1.0, 1.0), 2.0),
            "un chemin entièrement raccordé a été refusé");
    }

    private static void RejectsLongTargetConnector()
    {
        Require(!LearnedRoutePolicy.IsTrusted(Plan(1.0, 8.0), 1.0),
            "huit mètres non parcourus ont été présentés comme chemin appris");
    }

    private static void RejectsLongStartConnector()
    {
        Require(!LearnedRoutePolicy.IsTrusted(Plan(5.0, 1.0), 1.0),
            "cinq mètres non parcourus au départ ont été acceptés");
    }

    private static void RejectsUncertainPose()
    {
        Require(!LearnedRoutePolicy.IsTrusted(
                Plan(1.0, 1.0),
                LearnedRoutePolicy.MaximumLiveUncertaintyMeters + 0.01),
            "une pose trop incertaine utilise encore le chemin appris");
    }

    private static void ProgressNeverMovesBackward()
    {
        var waypoints = StraightWaypoints();
        var progressed = LearnedRouteProgress.Advance(Observation(5.1, 0, 0), waypoints, 1);
        var later = LearnedRouteProgress.Advance(Observation(2.0, 0, 0), waypoints, progressed);
        Require(progressed >= 2 && later >= progressed,
            "l'index du chemin a reculé");
    }

    private static void ProgressDoesNotCutCorner()
    {
        var waypoints = new[]
        {
            Waypoint(1, 0, 0, 0),
            Waypoint(2, 5, 0, 0),
            Waypoint(3, 5, 0, 5),
        };
        var index = LearnedRouteProgress.Advance(Observation(3.0, 0, 2.0), waypoints, 1);
        Require(index == 1, "le waypoint du virage a été sauté à distance");
    }

    private static void ProgressDoesNotSkipFloor()
    {
        var waypoints = new[]
        {
            Waypoint(1, 0, 0, 0),
            Waypoint(2, 2, 3, 0),
            Waypoint(3, 4, 3, 0),
        };
        var index = LearnedRouteProgress.Advance(Observation(2, 0, 0), waypoints, 1);
        Require(index == 1, "un waypoint trois mètres plus haut a été sauté");
    }

    private static void CacheCannotOutgrowTrustedConnector()
    {
        Require(
            LearnedRoutePolicy.CacheHorizontalMovementMeters
                <= LearnedRoutePolicy.MaximumConnectorMeters,
            "le cache autorise une déviation plus grande que le raccord appris");
        Require(
            LearnedRoutePolicy.CacheVerticalMovementMeters
                <= LearnedRouteProgress.ReachVerticalMeters,
            "le cache peut conserver un chemin sur un autre étage");
    }

    private static LearnedRoutePlan Plan(double startSnap, double targetSnap) => new(
        LearnedRouteKind.LearnedGraph,
        new LearnedRouteTarget("exit", "Sortie", 10, 0, 0),
        StraightWaypoints(),
        10,
        10,
        startSnap,
        targetSnap,
        0.9,
        LearnedRouteConfidence.High,
        LearnedRouteFallbackReason.None,
        UsesOnlyTraversedEdges: true);

    private static IReadOnlyList<LearnedRouteWaypoint> StraightWaypoints() => new[]
    {
        Waypoint(1, 0, 0, 0),
        Waypoint(2, 5, 0, 0),
        Waypoint(3, 10, 0, 0),
    };

    private static LearnedRouteWaypoint Waypoint(long id, double x, double y, double z) =>
        new(id, x, y, z, 0, 1.0, true);

    private static ScreenshotObservation Observation(double x, double y, double z) =>
        new("test.png", x, y, z, 0, 0, 0, 1, 0);

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
