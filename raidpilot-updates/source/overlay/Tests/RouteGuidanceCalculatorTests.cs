namespace RaidPilot.Overlay.Tests;

internal static class RouteGuidanceCalculatorTests
{
    internal const int TestCount = 8;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("guidage droit devant", PointsForward, failures);
        Run("flèche vers la droite", PointsRight, failures);
        Run("flèche vers la gauche selon le regard", PointsLeftRelativeToPlayer, failures);
        Run("rotation au passage de 360 degrés", WrapsAcrossNorth, failures);
        Run("zone d'arrivée sans flèche instable", StopsInsideArrivalRadius, failures);
        Run("waypoint proche garde une direction", NearbyWaypointKeepsDirection, failures);
        Run("étage différent non arrivé", DoesNotArriveOnAnotherFloor, failures);
        Run("rejet des coordonnées non finies", RejectsInvalidCoordinates, failures);
    }

    private static void PointsForward()
    {
        Require(RouteGuidanceCalculator.TryCalculate(10, 0, 20, 0, 10, 0, 120, out var guidance),
            "le guidage n'a pas été calculé");
        Near(guidance.DistanceMeters, 100, "distance");
        Near(guidance.TargetBearingDegrees, 0, "cap");
        Near(guidance.ArrowRotationDegrees, 0, "flèche");
        Require(guidance.HasDirection && !guidance.HasArrived, "état de guidage incorrect");
    }

    private static void PointsRight()
    {
        Require(RouteGuidanceCalculator.TryCalculate(0, 0, 0, 0, 25, 0, 0, out var guidance),
            "le guidage n'a pas été calculé");
        Near(guidance.DistanceMeters, 25, "distance");
        Near(guidance.TargetBearingDegrees, 90, "cap");
        Near(guidance.ArrowRotationDegrees, 90, "flèche");
    }

    private static void PointsLeftRelativeToPlayer()
    {
        Require(RouteGuidanceCalculator.TryCalculate(0, 0, 0, 90, 0, 0, 20, out var guidance),
            "le guidage n'a pas été calculé");
        Near(guidance.TargetBearingDegrees, 0, "cap");
        Near(guidance.ArrowRotationDegrees, -90, "flèche");
    }

    private static void WrapsAcrossNorth()
    {
        const double targetBearing = 10.0;
        const double distance = 100.0;
        var targetX = Math.Sin(targetBearing * Math.PI / 180.0) * distance;
        var targetZ = Math.Cos(targetBearing * Math.PI / 180.0) * distance;

        Require(RouteGuidanceCalculator.TryCalculate(0, 0, 0, 350, targetX, 0, targetZ, out var guidance),
            "le guidage n'a pas été calculé");
        Near(guidance.TargetBearingDegrees, 10, "cap", 0.000001);
        Near(guidance.ArrowRotationDegrees, 20, "flèche", 0.000001);
    }

    private static void StopsInsideArrivalRadius()
    {
        Require(RouteGuidanceCalculator.TryCalculate(1, 0, 1, -45, 4, 0, 5, out var guidance, 5),
            "le guidage n'a pas été calculé");
        Near(guidance.DistanceMeters, 5, "distance");
        Near(guidance.TargetBearingDegrees, 315, "cap stabilisé");
        Near(guidance.ArrowRotationDegrees, 0, "flèche stabilisée");
        Require(!guidance.HasDirection && guidance.HasArrived, "l'arrivée n'est pas détectée");
    }

    private static void NearbyWaypointKeepsDirection()
    {
        Require(RouteGuidanceCalculator.TryCalculate(
                0, 0, 0, 0, 3, 0, 0, out var guidance,
                arrivalRadiusMeters: 0.25,
                arrivalVerticalToleranceMeters: 0.75),
            "le guidage vers le waypoint n'a pas été calculé");
        Near(guidance.ArrowRotationDegrees, 90, "flèche du waypoint");
        Require(guidance.HasDirection && !guidance.HasArrived,
            "un waypoint à trois mètres ne doit pas neutraliser la flèche");
    }

    private static void DoesNotArriveOnAnotherFloor()
    {
        Require(RouteGuidanceCalculator.TryCalculate(
                0, 0, 0, 0, 0, 12, 0, out var guidance),
            "le guidage vertical n'a pas été calculé");
        Near(guidance.HorizontalDistanceMeters, 0, "distance horizontale");
        Near(guidance.VerticalDeltaMeters, 12, "écart vertical");
        Near(guidance.DistanceMeters, 12, "distance 3D");
        Require(!guidance.HasArrived, "un autre étage ne doit pas être marqué arrivé");
    }

    private static void RejectsInvalidCoordinates()
    {
        Require(!RouteGuidanceCalculator.TryCalculate(
                double.NaN, 0, 0, 0, 10, 0, 10, out _),
            "NaN ne doit pas produire de guidage");
        Require(!RouteGuidanceCalculator.TryCalculate(
                0, 0, 0, double.PositiveInfinity, 10, 0, 10, out _),
            "une rotation infinie ne doit pas produire de guidage");
        Require(!RouteGuidanceCalculator.TryCalculate(
                0, 0, 0, 0, 10, 0, 10, out _, -1),
            "un rayon d'arrivée négatif ne doit pas être accepté");
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

    private static void Near(double actual, double expected, string label, double tolerance = 0.001)
    {
        Require(Math.Abs(actual - expected) <= tolerance,
            $"{label} attendu {expected:F3}, reçu {actual:F3}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
