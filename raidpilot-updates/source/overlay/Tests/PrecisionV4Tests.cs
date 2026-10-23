using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class PrecisionV4Tests
{
    internal const int TestCount = 12;

    private static readonly long Timestamp = Stopwatch.Frequency * 10;
    private static RealtimePose Pose(double x = 0, double y = 0, double z = 0,
        double uncertainty = 3, double yaw = 0)
        => new(x, y, z, yaw, uncertainty, 4);

    private static VisualRelocalizationDecision Candidate(
        RealtimePose source,
        VisualKeyframePose target,
        VisualCorrectionMode mode = VisualCorrectionMode.Medium,
        VisualRelocalizationSource recognizer = VisualRelocalizationSource.Consensus,
        double confidence = 0.9,
        bool loopClosure = false)
    {
        var meters = Math.Sqrt(Math.Pow(source.X - target.X, 2)
            + Math.Pow(source.Z - target.Z, 2));
        var yaw = Math.Abs(((source.YawDegrees - target.YawDegrees + 540) % 360) - 180);
        return new VisualRelocalizationDecision(
            mode, recognizer, target, confidence, meters,
            Math.Abs(source.Y - target.Y), yaw,
            mode == VisualCorrectionMode.HardSnap ? 1 : 0.7,
            mode == VisualCorrectionMode.HardSnap ? 1 : 0.65,
            mode == VisualCorrectionMode.HardSnap ? double.PositiveInfinity : 1.75,
            mode == VisualCorrectionMode.HardSnap ? 180 : 8,
            loopClosure, LocalizationHealthState.Yellow, "synthetic");
    }

    internal static void RunAll(ICollection<string> errors)
    {
        Run("V4: la capture doit être vivante", () =>
        {
            var fusion = new PrecisionV4FusionEngine();
            var p = Pose();
            Require(!fusion.TryFuse("customs", Candidate(p,
                new VisualKeyframePose(1, 0, 1, 0)),
                p, false, Timestamp, out _));
        }, errors);
        Run("V4: le filtre conserve une micro-correction fiable", () =>
        {
            var fusion = new PrecisionV4FusionEngine();
            var p = Pose(uncertainty: 0.6);
            var d = Candidate(p, new VisualKeyframePose(0.25, 0, 0, 1),
                VisualCorrectionMode.Soft, confidence: 0.8);
            Require(fusion.TryFuse("customs", d, p, true, Timestamp,
                out var accepted) && accepted.ShouldCorrect
                && accepted.PositionGain <= d.PositionGain);
        }, errors);
        Run("V4: gain Kalman réduit la confiance sur les observations bruyantes", () =>
        {
            var p = Pose(uncertainty: 0.5);
            var d = Candidate(p, new VisualKeyframePose(.4, 0, 0, 0),
                VisualCorrectionMode.Soft, VisualRelocalizationSource.LowResolution,
                confidence: 0.7);
            Require(new PrecisionV4FusionEngine().TryFuse("woods", d, p, true,
                Timestamp, out var accepted)
                && accepted.PositionGain < 0.1
                && accepted.PositionGain >= 0);
        }, errors);
        Run("V4: un grand snap sans preuve indépendante est refusé", () =>
        {
            var p = Pose(uncertainty: 10);
            var d = Candidate(p, new VisualKeyframePose(9, 0, 0, 0),
                VisualCorrectionMode.HardSnap, VisualRelocalizationSource.SuperKeyframe,
                confidence: .95);
            Require(!new PrecisionV4FusionEngine().TryFuse("customs", d, p, true,
                Timestamp, out _));
        }, errors);
        Run("V4: loop closure fort autorisé mais translation bornée", () =>
        {
            var p = Pose(uncertainty: 10);
            var d = Candidate(p, new VisualKeyframePose(15, 0, 0, 25),
                VisualCorrectionMode.HardSnap, VisualRelocalizationSource.SuperKeyframe,
                confidence: .98, loopClosure: true);
            var f = new PrecisionV4FusionEngine();
            Require(f.TryFuse("customs", d, p, true, Timestamp, out var accepted)
                && accepted.MaximumTranslationStepMeters <= 8
                && accepted.MaximumYawStepDegrees <= 25
                && f.LimitedHardSnaps == 1);
        }, errors);
        Run("V4: une fausse relocalisation loin du modèle est bloquée", () =>
        {
            var p = Pose(uncertainty: 1);
            var d = Candidate(p, new VisualKeyframePose(100, 0, 0, 0),
                VisualCorrectionMode.HardSnap, confidence: 0.99);
            Require(!new PrecisionV4FusionEngine().TryFuse("customs", d, p, true,
                Timestamp, out _));
        }, errors);
        Run("V4: une correspondance sur un autre étage est refusée", () =>
        {
            var p = Pose(y: 0);
            var d = Candidate(p, new VisualKeyframePose(1, 6, 0, 0));
            Require(!new PrecisionV4FusionEngine().TryFuse("customs", d, p, true,
                Timestamp, out _));
        }, errors);
        Run("V4: un monde sans polygones vérifiés n'invente pas de murs", () =>
        {
            var p = Pose(uncertainty: 5);
            var d = Candidate(p, new VisualKeyframePose(1, 0, 0, 0));
            Require(new PrecisionV4FusionEngine().TryFuse("customs", d, p, true,
                Timestamp, out _));
        }, errors);
        Run("V4: les corridors certifiés refusent une téléportation entre zones", () =>
        {
            var poly = new[] { new PrecisionV4Point(0, 0), new PrecisionV4Point(10, 0),
                new PrecisionV4Point(10, 10), new PrecisionV4Point(0, 10) };
            var poly2 = poly.Select(v => v with { X = v.X + 20 }).ToArray();
            var guard = new PrecisionV4GeometryGuard(new[]
            {
                new PrecisionV4WalkableArea("customs", -2, 2, poly),
                new PrecisionV4WalkableArea("customs", -2, 2, poly2),
            });
            Require(!guard.Allows("customs", Pose(2, 0, 2),
                new VisualKeyframePose(22, 0, 2, 0)));
            Require(guard.Allows("customs", Pose(2, 0, 2),
                new VisualKeyframePose(4, 0, 2, 0)));
        }, errors);
        Run("V4: contour concave interdit le franchissement d'un mur", () =>
        {
            var shape = new[]
            {
                new PrecisionV4Point(0, 0), new PrecisionV4Point(6, 0),
                new PrecisionV4Point(6, 6), new PrecisionV4Point(4, 6),
                new PrecisionV4Point(4, 2), new PrecisionV4Point(2, 2),
                new PrecisionV4Point(2, 6), new PrecisionV4Point(0, 6),
            };
            var guard = new PrecisionV4GeometryGuard(new[]
                { new PrecisionV4WalkableArea("woods", -1, 1, shape) });
            Require(!guard.Allows("woods", Pose(1, 0, 5),
                new VisualKeyframePose(5, 0, 5, 0)));
        }, errors);
        Run("V4: calibration bloquée après un saut de dérive isolé", () =>
        {
            var samples = new[]
            {
                new PrecisionDriftSample(true, 1, 2, 20, 2),
                new PrecisionDriftSample(true, 1.2, 2, 20, 2),
                new PrecisionDriftSample(true, 0.8, 2, 20, 2),
                new PrecisionDriftSample(true, 18, 2, 20, 2),
            };
            Require(!PrecisionV4CalibrationSafety.MayLearn(samples));
            Require(PrecisionV4CalibrationSafety.MayLearn(samples[..3]));
        }, errors);
        Run("V4: plusieurs intervalles propres conservent l'apprentissage", () =>
        {
            var samples = Enumerable.Range(0, 5)
                .Select(_ => new PrecisionDriftSample(true, 1, 1, 20, 2))
                .ToArray();
            Require(PrecisionV4CalibrationSafety.MayLearn(samples));
            var f = new PrecisionV4FusionEngine();
            f.Reset();
            Require(f.Accepted == 0 && f.Rejected == 0);
        }, errors);
    }

    private static void Run(string name, Action action, ICollection<string> errors)
    {
        try { action(); Console.WriteLine("OK  " + name); }
        catch (Exception exception) { errors.Add(name + " : " + exception.Message); }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Precision V4 assertion failed");
    }
}
