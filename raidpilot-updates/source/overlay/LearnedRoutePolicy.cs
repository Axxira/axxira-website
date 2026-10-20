namespace RaidPilot.Overlay;

/// <summary>
/// Règles prudentes qui empêchent de présenter un raccord de proximité comme
/// un passage réellement appris.
/// </summary>
internal static class LearnedRoutePolicy
{
    internal const double MaximumLiveUncertaintyMeters = 4.0;
    internal const double MaximumConnectorMeters = 1.5;
    internal const double CacheLifetimeSeconds = 1.0;
    internal const double CacheHorizontalMovementMeters = 1.5;
    internal const double CacheVerticalMovementMeters = 0.75;
    internal const double CacheUncertaintyDeltaMeters = 0.75;

    internal static bool IsTrusted(
        LearnedRoutePlan? plan,
        double currentUncertaintyMeters)
    {
        return plan is
            {
                Kind: LearnedRouteKind.LearnedGraph,
                UsesOnlyTraversedEdges: true,
                Confidence: LearnedRouteConfidence.Medium or LearnedRouteConfidence.High,
            }
            && plan.Waypoints.Count >= 2
            && double.IsFinite(currentUncertaintyMeters)
            && currentUncertaintyMeters is >= 0.0 and <= MaximumLiveUncertaintyMeters
            && double.IsFinite(plan.StartSnapDistanceMeters)
            && plan.StartSnapDistanceMeters is >= 0.0 and <= MaximumConnectorMeters
            && double.IsFinite(plan.TargetSnapDistanceMeters)
            && plan.TargetSnapDistanceMeters is >= 0.0 and <= MaximumConnectorMeters;
    }
}
