namespace RaidPilot.Overlay.Tests;

internal static class RealtimePoseHistoryTests
{
    internal const int TestCount = 6;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("historique vide et hors plage", RejectsEmptyAndOutOfRangeQueries, failures);
        Run("interpolation de la pose horodatée", InterpolatesAllPoseFields, failures);
        Run("interpolation du yaw par le chemin court", InterpolatesYawAcrossNorth, failures);
        Run("tampon circulaire borné", EvictsOldestPoseAtCapacity, failures);
        Run("horodatages strictement croissants", RejectsNonIncreasingTimestamps, failures);
        Run("remise à zéro propre", ResetsForANewTimeline, failures);
    }

    private static void RejectsEmptyAndOutOfRangeQueries()
    {
        var history = new RealtimePoseHistory(4);
        Require(!history.TryGetPoseAt(10, out _), "un historique vide ne devrait pas répondre");

        history.Add(10, Pose(1, 2, 3, 4, 5, 6));
        history.Add(20, Pose(2, 3, 4, 5, 6, 7));

        Require(!history.TryGetPoseAt(9, out _), "une requête avant la plage devrait échouer");
        Require(!history.TryGetPoseAt(21, out _), "une requête après la plage devrait échouer");
        Require(history.TryGetPoseAt(10, out var exact), "la borne exacte devrait être trouvée");
        Require(exact == Pose(1, 2, 3, 4, 5, 6), "la pose exacte a été modifiée");
    }

    private static void InterpolatesAllPoseFields()
    {
        var history = new RealtimePoseHistory(4);
        history.Add(100, Pose(0, 10, -20, 40, 1, 2));
        history.Add(300, Pose(20, 30, 40, 80, 5, 10));

        Require(history.TryGetPoseAt(150, out var pose), "l'interpolation devrait réussir");
        Near(pose.X, 5, "x");
        Near(pose.Y, 15, "y");
        Near(pose.Z, -5, "z");
        Near(pose.YawDegrees, 50, "yaw");
        Near(pose.PositionUncertaintyMeters, 2, "incertitude de position");
        Near(pose.YawUncertaintyDegrees, 4, "incertitude de cap");
    }

    private static void InterpolatesYawAcrossNorth()
    {
        var clockwise = new RealtimePoseHistory(4);
        clockwise.Add(0, Pose(yaw: 359));
        clockwise.Add(100, Pose(yaw: 1));
        Require(clockwise.TryGetPoseAt(50, out var clockwiseMidpoint), "le milieu 359°→1° manque");
        Near(clockwiseMidpoint.YawDegrees, 0, "yaw 359°→1°");

        var counterClockwise = new RealtimePoseHistory(4);
        counterClockwise.Add(0, Pose(yaw: 1));
        counterClockwise.Add(100, Pose(yaw: 359));
        Require(counterClockwise.TryGetPoseAt(50, out var counterClockwiseMidpoint), "le milieu 1°→359° manque");
        Near(counterClockwiseMidpoint.YawDegrees, 0, "yaw 1°→359°");
    }

    private static void EvictsOldestPoseAtCapacity()
    {
        var history = new RealtimePoseHistory(3);
        history.Add(10, Pose(x: 10));
        history.Add(20, Pose(x: 20));
        history.Add(30, Pose(x: 30));
        history.Add(40, Pose(x: 40));

        Require(history.Count == 3, "le tampon a dépassé sa capacité");
        Require(history.Capacity == 3, "la capacité publiée est incorrecte");
        Require(!history.TryGetPoseAt(10, out _), "la plus ancienne pose aurait dû être évincée");
        Require(history.TryGetPoseAt(20, out var oldest) && oldest.X == 20,
            "la nouvelle borne inférieure est incorrecte");
        Require(history.TryGetPoseAt(35, out var interpolated), "l'interpolation après rotation du tampon a échoué");
        Near(interpolated.X, 35, "x après rotation");
    }

    private static void RejectsNonIncreasingTimestamps()
    {
        var history = new RealtimePoseHistory(3);
        history.Add(100, Pose());

        RequireThrows<ArgumentOutOfRangeException>(() => history.Add(100, Pose()),
            "un horodatage identique devrait être refusé");
        RequireThrows<ArgumentOutOfRangeException>(() => history.Add(99, Pose()),
            "un horodatage antérieur devrait être refusé");
        Require(history.Count == 1, "un ajout refusé a modifié le tampon");
    }

    private static void ResetsForANewTimeline()
    {
        var history = new RealtimePoseHistory(3);
        history.Add(1_000, Pose(x: 1));
        history.Add(2_000, Pose(x: 2));

        history.Reset();

        Require(history.Count == 0, "Reset n'a pas vidé le tampon");
        Require(!history.TryGetPoseAt(1_000, out _), "une ancienne pose survit au Reset");
        history.Add(5, Pose(x: 5));
        Require(history.TryGetPoseAt(5, out var pose) && pose.X == 5,
            "une nouvelle origine de temps devrait être acceptée");
    }

    private static RealtimePose Pose(
        double x = 0,
        double y = 0,
        double z = 0,
        double yaw = 0,
        double positionUncertainty = 0,
        double yawUncertainty = 0) =>
        new(x, y, z, yaw, positionUncertainty, yawUncertainty);

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

    private static void Near(double actual, double expected, string label, double tolerance = 0.000001)
    {
        Require(Math.Abs(actual - expected) <= tolerance,
            $"{label} attendu {expected:F6}, reçu {actual:F6}");
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
