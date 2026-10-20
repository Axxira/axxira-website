namespace RaidPilot.Overlay;

/// <summary>
/// Avance uniquement vers l'avant dans un chemin appris. Un étage voisin ou
/// un virage coupé à distance ne peut pas faire sauter un waypoint.
/// </summary>
internal static class LearnedRouteProgress
{
    internal const double ReachHorizontalMeters = 1.25;
    internal const double ReachVerticalMeters = 0.75;

    internal static int Advance(
        ScreenshotObservation observation,
        IReadOnlyList<LearnedRouteWaypoint> waypoints,
        int minimumIndex)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(waypoints);

        var index = Math.Clamp(minimumIndex, 0, waypoints.Count);
        while (index < waypoints.Count)
        {
            var waypoint = waypoints[index];
            if (IsReached(observation, waypoint)
                || HasPassedTowardNext(observation, waypoints, index))
            {
                index++;
                continue;
            }

            break;
        }

        return index;
    }

    private static bool IsReached(
        ScreenshotObservation observation,
        LearnedRouteWaypoint waypoint)
    {
        var deltaX = waypoint.X - observation.X;
        var deltaZ = waypoint.Z - observation.Z;
        return Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ))
                <= ReachHorizontalMeters
            && Math.Abs(waypoint.Y - observation.Y) <= ReachVerticalMeters;
    }

    private static bool HasPassedTowardNext(
        ScreenshotObservation observation,
        IReadOnlyList<LearnedRouteWaypoint> waypoints,
        int index)
    {
        if (index + 1 >= waypoints.Count)
        {
            return false;
        }

        var current = waypoints[index];
        var next = waypoints[index + 1];
        var segmentX = next.X - current.X;
        var segmentZ = next.Z - current.Z;
        var segmentLengthSquared = (segmentX * segmentX) + (segmentZ * segmentZ);
        if (!double.IsFinite(segmentLengthSquared) || segmentLengthSquared < 0.01)
        {
            return false;
        }

        var playerX = observation.X - current.X;
        var playerZ = observation.Z - current.Z;
        var projection = ((playerX * segmentX) + (playerZ * segmentZ))
            / segmentLengthSquared;
        if (!double.IsFinite(projection) || projection <= 0.0)
        {
            return false;
        }

        var projectedX = current.X + (projection * segmentX);
        var projectedZ = current.Z + (projection * segmentZ);
        var lateralX = observation.X - projectedX;
        var lateralZ = observation.Z - projectedZ;
        var lateralDistance = Math.Sqrt((lateralX * lateralX) + (lateralZ * lateralZ));
        var clampedProjection = Math.Clamp(projection, 0.0, 1.0);
        var projectedY = current.Y + (clampedProjection * (next.Y - current.Y));
        return lateralDistance <= ReachHorizontalMeters
            && Math.Abs(observation.Y - projectedY) <= ReachVerticalMeters;
    }
}
