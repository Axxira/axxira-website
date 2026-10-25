using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class CortexLandmarkMemoryTests
{
    internal const int TestCount = 9;
    private static readonly long Origin = Stopwatch.Frequency * 10;

    private static long At(double elapsedSeconds) =>
        Origin + (long)(elapsedSeconds * Stopwatch.Frequency);

    private static VisualKeyframePose Pose(
        double x = 0, double y = 0, double z = 0, double yaw = 0) =>
        new(x, y, z, yaw);

    internal static void RunAll(ICollection<string> failures)
    {
        Run("CORTEX mémoire: même identité ne compte qu'une fois", () =>
        {
            var m = new CortexLandmarkMemory();
            Require(m.Observe(At(0), 1, 10, Pose())
                == CortexLandmarkResult.NewLandmark);
            Require(m.Observe(At(.5), 1, 10, Pose(x: .2))
                == CortexLandmarkResult.Revisited);
            Require(m.Count == 1 && m.Repeated == 1);
            Require(m.CountRecentDistinct(At(1), 5) == 1);
        }, failures);

        Run("CORTEX mémoire: deux lieux séparés sont indépendants", () =>
        {
            var m = new CortexLandmarkMemory();
            m.Observe(At(0), 1, 10, Pose());
            m.Observe(At(1), 2, 20, Pose(x: 5));
            Require(m.CountRecentDistinct(At(1.5), 5) == 2);
        }, failures);

        Run("CORTEX mémoire: identifiants différents sur même lieu ne suffisent pas", () =>
        {
            var m = new CortexLandmarkMemory();
            m.Observe(At(0), 1, 10, Pose());
            m.Observe(At(1), 2, 20, Pose(x: .4));
            Require(m.Count == 2 && m.CountRecentDistinct(At(2), 5) == 1);
        }, failures);

        Run("CORTEX mémoire: un autre étage ne crée pas deux repères locaux", () =>
        {
            var m = new CortexLandmarkMemory();
            m.Observe(At(0), 1, 10, Pose());
            m.Observe(At(1), 2, 20, Pose(x: 9, y: 6));
            Require(m.CountRecentDistinct(At(2), 5) == 1);
        }, failures);

        Run("CORTEX mémoire: une pose incohérente pour même identité est refusée", () =>
        {
            var m = new CortexLandmarkMemory();
            m.Observe(At(0), 1, 10, Pose());
            Require(m.Observe(At(1), 1, 10, Pose(x: 50))
                == CortexLandmarkResult.ConflictingPose);
            Require(m.Count == 1 && m.Conflicting == 1);
        }, failures);

        Run("CORTEX mémoire: variation angulaire cyclique 359/1 reste cohérente", () =>
        {
            var m = new CortexLandmarkMemory();
            m.Observe(At(0), 1, 10, Pose(yaw: 359));
            Require(m.Observe(At(1), 1, 10, Pose(yaw: 1))
                == CortexLandmarkResult.Revisited);
            Require(m.Observe(At(2), 1, 10, Pose(yaw: 90))
                == CortexLandmarkResult.ConflictingPose);
        }, failures);

        Run("CORTEX mémoire: aucun doublon temporel ou identifiant nul", () =>
        {
            var m = new CortexLandmarkMemory();
            Require(m.Observe(At(0), 0, 10, Pose())
                == CortexLandmarkResult.Invalid);
            Require(m.Observe(At(0), 1, 0, Pose())
                == CortexLandmarkResult.Invalid);
            m.Observe(At(1), 1, 10, Pose());
            Require(m.Observe(At(1), 1, 10, Pose())
                == CortexLandmarkResult.Invalid);
            Require(m.Observe(At(.5), 1, 10, Pose())
                == CortexLandmarkResult.Invalid);
        }, failures);

        Run("CORTEX mémoire: la confiance visuelle expire", () =>
        {
            var m = new CortexLandmarkMemory();
            m.Observe(At(0), 1, 10, Pose());
            Require(m.CountRecentDistinct(At(5.1), 5) == 0);
            Require(m.CountRecentDistinct(At(-1), 5) == 0);
            Require(m.CountRecentDistinct(At(3), double.NaN) == 0);
        }, failures);

        Run("CORTEX mémoire: capacité bornée et reset sans état résiduel", () =>
        {
            var m = new CortexLandmarkMemory();
            for (var i = 1; i <= 55; i++)
                m.Observe(At(i), i, i + 100,
                    Pose(x: i * 4));
            Require(m.Count == 48 && m.Evicted == 7);
            m.Reset();
            Require(m.Count == 0 && m.Repeated == 0 && m.Conflicting == 0
                && m.Evicted == 0);
        }, failures);
    }

    private static void Run(string title, Action test, ICollection<string> failures)
    {
        try { test(); Console.WriteLine("OK  " + title); }
        catch (Exception error) { failures.Add(title + " : " + error.Message); }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion mémoire CORTEX");
    }
}
