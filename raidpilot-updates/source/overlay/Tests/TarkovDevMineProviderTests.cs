namespace RaidPilot.Overlay.Tests;

internal static class TarkovDevMineProviderTests
{
    internal const int TestCount = 8;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("mines: filtre uniquement minefield", ParsesOnlyMinefields, failures);
        Run("mines: position intérieure critique", InsidePolygonIsCritical, failures);
        Run("mines: distance au bord et non au centre", UsesPolygonEdgeDistance, failures);
        Run("mines: position éloignée reste sûre", FarPositionIsSafe, failures);
        Run("mines: incertitude avance l'alerte", UncertaintyExpandsSafetyMargin, failures);
        Run("mines: étage vertical différent ignoré", VerticalRangePreventsFalseAlert, failures);
        Run("mines: absence hauteur reste prudente", MissingVerticalBoundsRemainRelevant, failures);
        Run("mines: hystérésis empêche le clignotement frontière", HysteresisKeepsAlertStable, failures);
    }

    private static void ParsesOnlyMinefields()
    {
        var zones = TarkovDevMineProvider.Parse(
            SampleMapsJson(),
            "customs");

        Require(zones.Count == 1, $"1 mine attendue, reçu {zones.Count}");
        var zone = zones[0];
        Require(zone.Outline.Count == 4, "contour mine incomplet");
        Near(zone.X, 50, "centre X");
        Near(zone.Y, 5, "centre Y");
        Near(zone.Z, 50, "centre Z");
        Near(zone.Top ?? double.NaN, 12, "top");
        Near(zone.Bottom ?? double.NaN, -2, "bottom");
    }

    private static void InsidePolygonIsCritical()
    {
        var zone = SquareZone(top: 10, bottom: -10);
        var result = TarkovDevMineProvider.EvaluateProximity(
            Pose(5, 0, 5, uncertainty: 0.5),
            new[] { zone });

        Require(result.Critical, "être dans un champ de mines doit être critique");
        Require(result.InsideMinefield, "inside minefield attendu");
        Near(result.DistanceMeters, 0, "distance intérieure");
    }

    private static void UsesPolygonEdgeDistance()
    {
        var zone = new MineHazardZone(
            "long",
            50,
            0,
            5,
            new[]
            {
                new MineHazardPoint(0, 0),
                new MineHazardPoint(100, 0),
                new MineHazardPoint(100, 10),
                new MineHazardPoint(0, 10),
            },
            null,
            null);

        var result = TarkovDevMineProvider.EvaluateProximity(
            Pose(2, 0, -8, uncertainty: 0),
            new[] { zone });

        Near(result.DistanceMeters, 8, "distance au bord");
        Require(result.Critical, "8 m du bord devrait être critique");
    }

    private static void FarPositionIsSafe()
    {
        var result = TarkovDevMineProvider.EvaluateProximity(
            Pose(40, 0, 40, uncertainty: 0),
            new[] { SquareZone() });

        Require(!result.Critical, "une mine à plus de 12 m ne doit pas alerter");
        Require(result.DistanceMeters > 12, "distance éloignée attendue");
    }

    private static void UncertaintyExpandsSafetyMargin()
    {
        var zone = SquareZone();
        var exact = TarkovDevMineProvider.EvaluateProximity(
            Pose(5, 0, -15, uncertainty: 0),
            new[] { zone });
        var uncertain = TarkovDevMineProvider.EvaluateProximity(
            Pose(5, 0, -15, uncertainty: 4),
            new[] { zone });

        Require(!exact.Critical, "15 m précis devrait rester sûr");
        Require(uncertain.Critical, "15 m avec 4 m d'incertitude devrait prévenir");
        Near(uncertain.EffectiveDistanceMeters, 11, "distance effective");
    }

    private static void VerticalRangePreventsFalseAlert()
    {
        var zone = SquareZone(top: 5, bottom: -5);
        var result = TarkovDevMineProvider.EvaluateProximity(
            Pose(5, 20, 5, uncertainty: 1),
            new[] { zone });

        Require(!result.Critical, "mine très loin verticalement ne doit pas alerter");
        Require(double.IsPositiveInfinity(result.DistanceMeters),
            "zone verticale devrait être exclue");
    }

    private static void MissingVerticalBoundsRemainRelevant()
    {
        var zone = SquareZone(top: null, bottom: null);
        var result = TarkovDevMineProvider.EvaluateProximity(
            Pose(5, 100, 5, uncertainty: 0),
            new[] { zone });

        Require(result.Critical,
            "sans bornes verticales l'alerte doit rester prudente");
    }

    private static void HysteresisKeepsAlertStable()
    {
        var zone = SquareZone();
        var pose = Pose(5, 0, -14, uncertainty: 0);

        var initial = TarkovDevMineProvider.EvaluateProximity(
            pose,
            new[] { zone },
            alertAlreadyActive: false);
        var alreadyActive = TarkovDevMineProvider.EvaluateProximity(
            pose,
            new[] { zone },
            alertAlreadyActive: true);

        Require(!initial.Critical,
            "14 m ne doit pas allumer une nouvelle alerte");
        Require(alreadyActive.Critical,
            "14 m doit garder une alerte déjà active jusqu'à 16 m");
    }

    private static MineHazardZone SquareZone(
        double? top = null,
        double? bottom = null) => new(
        "square",
        5,
        0,
        5,
        new[]
        {
            new MineHazardPoint(0, 0),
            new MineHazardPoint(10, 0),
            new MineHazardPoint(10, 10),
            new MineHazardPoint(0, 10),
        },
        top,
        bottom);

    private static RealtimePose Pose(
        double x,
        double y,
        double z,
        double uncertainty) =>
        new(x, y, z, 0, uncertainty, 1);

    private static string SampleMapsJson() =>
        """
        {
          "data": {
            "maps": {
              "customs": {
                "normalizedName": "customs",
                "hazards": [
                  {
                    "hazardType": "minefield",
                    "name": "DamageType_Landmine",
                    "position": { "x": 50, "y": 5, "z": 50 },
                    "outline": [
                      { "x": 40, "y": 0, "z": 40 },
                      { "x": 60, "y": 0, "z": 40 },
                      { "x": 60, "y": 0, "z": 60 },
                      { "x": 40, "y": 0, "z": 60 }
                    ],
                    "top": 12,
                    "bottom": -2
                  },
                  {
                    "hazardType": "sniper",
                    "name": "ScavRole/Marksman",
                    "position": { "x": 0, "y": 0, "z": 0 },
                    "outline": [
                      { "x": -10, "y": 0, "z": -10 },
                      { "x": 10, "y": 0, "z": -10 },
                      { "x": 10, "y": 0, "z": 10 },
                      { "x": -10, "y": 0, "z": 10 }
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
            Console.Error.WriteLine($"ERR {name}: {exception.Message}");
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
