namespace RaidPilot.Overlay.Tests;

internal static class TarkovDevBoundaryProviderTests
{
    internal const int TestCount = 6;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("limites: filtre uniquement sniper", ParsesOnlySniperBoundaries, failures);
        Run("limites: pré-alerte à trente mètres", WarnsBeforeBoundary, failures);
        Run("limites: critique à quatorze mètres", CriticalNearBoundary, failures);
        Run("limites: intérieur interdit critique", InsideBoundaryIsCritical, failures);
        Run("limites: incertitude avance l'alerte", UncertaintyExpandsBoundaryMargin, failures);
        Run("limites: hystérésis stabilise la sortie", BoundaryHysteresisIsStable, failures);
    }

    private static void ParsesOnlySniperBoundaries()
    {
        var zones = TarkovDevBoundaryProvider.Parse(
            SampleMapsJson(),
            "customs");
        Require(zones.Count == 1, $"1 zone sniper attendue, reçu {zones.Count}");
        Require(zones[0].Outline.Count == 4, "contour sniper incomplet");
    }

    private static void WarnsBeforeBoundary()
    {
        var result = TarkovDevBoundaryProvider.EvaluateProximity(
            Pose(5, -25, uncertainty: 0),
            new[] { SquareZone() });

        Require(result.Warning, "25 m du bord doit prévenir");
        Require(!result.Critical, "25 m ne doit pas encore être critique");
        Near(result.DistanceMeters, 25, "distance pré-alerte");
    }

    private static void CriticalNearBoundary()
    {
        var result = TarkovDevBoundaryProvider.EvaluateProximity(
            Pose(5, -10, uncertainty: 0),
            new[] { SquareZone() });

        Require(result.Warning && result.Critical,
            "10 m du bord doit être critique");
    }

    private static void InsideBoundaryIsCritical()
    {
        var result = TarkovDevBoundaryProvider.EvaluateProximity(
            Pose(5, 5, uncertainty: 0),
            new[] { SquareZone() });

        Require(result.Critical && result.InsideDangerZone,
            "entrer dans la zone sniper doit forcer DEMI-TOUR");
        Near(result.DistanceMeters, 0, "distance intérieure");
    }

    private static void UncertaintyExpandsBoundaryMargin()
    {
        var precise = TarkovDevBoundaryProvider.EvaluateProximity(
            Pose(5, -34, uncertainty: 0),
            new[] { SquareZone() });
        var uncertain = TarkovDevBoundaryProvider.EvaluateProximity(
            Pose(5, -34, uncertainty: 5),
            new[] { SquareZone() });

        Require(!precise.Warning, "34 m précis devrait rester hors alerte");
        Require(uncertain.Warning,
            "34 m avec 5 m d'incertitude devrait prévenir");
    }

    private static void BoundaryHysteresisIsStable()
    {
        var zone = SquareZone();
        var warningPose = Pose(5, -33, uncertainty: 0);
        var initial = TarkovDevBoundaryProvider.EvaluateProximity(
            warningPose,
            new[] { zone },
            warningAlreadyActive: false);
        var active = TarkovDevBoundaryProvider.EvaluateProximity(
            warningPose,
            new[] { zone },
            warningAlreadyActive: true);

        Require(!initial.Warning, "33 m ne doit pas allumer une nouvelle pré-alerte");
        Require(active.Warning, "33 m doit conserver une alerte déjà active");
    }

    private static BoundaryHazardZone SquareZone() => new(
        "sniper-square",
        5,
        0,
        5,
        new[]
        {
            new BoundaryHazardPoint(0, 0),
            new BoundaryHazardPoint(10, 0),
            new BoundaryHazardPoint(10, 10),
            new BoundaryHazardPoint(0, 10),
        },
        null,
        null);

    private static RealtimePose Pose(
        double x,
        double z,
        double uncertainty) =>
        new(x, 0, z, 0, uncertainty, 1);

    private static string SampleMapsJson() =>
        """
        {
          "data": {
            "maps": {
              "customs": {
                "normalizedName": "customs",
                "hazards": [
                  {
                    "hazardType": "sniper",
                    "name": "ScavRole/Marksman",
                    "position": { "x": 5, "y": 0, "z": 5 },
                    "outline": [
                      { "x": 0, "y": 0, "z": 0 },
                      { "x": 10, "y": 0, "z": 0 },
                      { "x": 10, "y": 0, "z": 10 },
                      { "x": 0, "y": 0, "z": 10 }
                    ]
                  },
                  {
                    "hazardType": "minefield",
                    "name": "DamageType_Landmine",
                    "position": { "x": 50, "y": 0, "z": 50 },
                    "outline": [
                      { "x": 40, "y": 0, "z": 40 },
                      { "x": 60, "y": 0, "z": 40 },
                      { "x": 60, "y": 0, "z": 60 },
                      { "x": 40, "y": 0, "z": 60 }
                    ]
                  }
                ]
              }
            }
          }
        }
        """;

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

    private static void Near(
        double actual,
        double expected,
        string label,
        double tolerance = 0.000001)
    {
        if (!double.IsFinite(actual)
            || Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException(
                $"{label}: attendu {expected}, reçu {actual}");
        }
    }
}
