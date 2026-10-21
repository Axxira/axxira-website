using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class VisualRelocalizationEngineV2Tests
{
    internal const int TestCount = 8;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("relocalisation v2: santé se dégrade avec dérive et âge", HealthDegradesWithDriftAndAge, failures);
        Run("relocalisation v2: cadence accélère en rouge", LowResolutionCadenceAcceleratesWhenRed, failures);
        Run("relocalisation v2: haute résolution seulement quand utile", HighResolutionRunsWhenUseful, failures);
        Run("relocalisation v2: keyframe legacy produit micro-correction", LegacyLowResolutionCanSoftCorrect, failures);
        Run("relocalisation v2: super-keyframe requis quand disponible", RequiresSuperKeyframeWhenAvailable, failures);
        Run("relocalisation v2: consensus produit rattrapage amorti", ConsensusProducesMediumCorrection, failures);
        Run("relocalisation v2: désaccord visuel rejeté", RejectsCrossRecognizerDisagreement, failures);
        Run("relocalisation v2: loop closure fort autorise snap", StrongGlobalLoopClosureAllowsHardSnap, failures);
    }

    private static void HealthDegradesWithDriftAndAge()
    {
        var engine = new VisualRelocalizationEngineV2();
        var start = Stopwatch.Frequency;
        engine.BeginSession(start);
        engine.NoteAbsoluteAnchor(start);

        Require(
            engine.ObserveTracking(Pose(0, 0, 0, 0, 1.0), start)
                == LocalizationHealthState.Green,
            "ancre fraîche devrait être verte");
        Require(
            engine.ObserveTracking(Pose(0, 0, 0, 0, 2.0), start + Seconds(10))
                == LocalizationHealthState.Yellow,
            "10 s et 2 m devraient être jaunes");
        Require(
            engine.ObserveTracking(Pose(0, 0, 0, 0, 5.0), start + Seconds(30))
                == LocalizationHealthState.Orange,
            "30 s et 5 m devraient être orange");
        Require(
            engine.ObserveTracking(Pose(0, 0, 0, 0, 9.0), start + Seconds(60))
                == LocalizationHealthState.Red,
            "forte dérive devrait être rouge");
    }

    private static void LowResolutionCadenceAcceleratesWhenRed()
    {
        var engine = new VisualRelocalizationEngineV2();
        var start = Stopwatch.Frequency;
        engine.BeginSession(start);
        engine.NoteAbsoluteAnchor(start);
        var redAt = start + Seconds(60);
        engine.ObserveTracking(Pose(0, 0, 0, 0, 10), redAt);

        Require(engine.ShouldRunLowResolution(true, redAt),
            "première recherche rouge devrait être immédiate");
        Require(!engine.ShouldRunLowResolution(true, redAt + Seconds(0.10)),
            "100 ms est encore trop tôt pour le seuil rouge");
        Require(engine.ShouldRunLowResolution(true, redAt + Seconds(0.13)),
            "130 ms devrait relancer la recherche rouge");
    }

    private static void HighResolutionRunsWhenUseful()
    {
        var engine = new VisualRelocalizationEngineV2();
        var start = Stopwatch.Frequency;
        engine.BeginSession(start);
        engine.NoteAbsoluteAnchor(start);

        Require(!engine.ShouldRunHighResolution(
                Pose(0, 0, 0, 0, 0.5),
                superKeyframesAvailable: true,
                start),
            "une pose verte précise ne doit pas payer la haute résolution");

        var later = start + Seconds(25);
        Require(engine.ShouldRunHighResolution(
                Pose(0, 0, 0, 0, 5.0),
                superKeyframesAvailable: true,
                later),
            "une pose orange devrait déclencher la haute résolution");
        Require(!engine.ShouldRunHighResolution(
                Pose(0, 0, 0, 0, 5.0),
                superKeyframesAvailable: true,
                later + Seconds(0.1)),
            "la cadence haute résolution doit rester bornée");
    }

    private static void LegacyLowResolutionCanSoftCorrect()
    {
        var engine = ReadyEngine(out var now);
        var estimate = Pose(10, 1, 20, 15, 1.2);
        var target = new VisualKeyframePose(10.25, 1.0, 20.20, 16);

        var decision = engine.Evaluate(
            estimate,
            LowConfirmed(target),
            SuperKeyframeRecognition.NoMatch(),
            superKeyframesAvailable: false,
            now);

        Require(decision.Mode == VisualCorrectionMode.Soft,
            $"micro-correction attendue, reçu {decision.Mode}");
        Require(decision.Source == VisualRelocalizationSource.LowResolution,
            "source legacy incorrecte");
        Require(decision.Confidence >= 0.68,
            "confiance legacy insuffisante");
    }

    private static void RequiresSuperKeyframeWhenAvailable()
    {
        var engine = ReadyEngine(out var now);
        var target = new VisualKeyframePose(1, 0, 1, 0);

        var decision = engine.Evaluate(
            Pose(1.2, 0, 1.1, 0, 2),
            LowConfirmed(target),
            SuperKeyframeRecognition.NoMatch(),
            superKeyframesAvailable: true,
            now);

        Require(decision.Mode == VisualCorrectionMode.None,
            "une base super-keyframe existante doit rester l'arbitre");
    }

    private static void ConsensusProducesMediumCorrection()
    {
        var engine = ReadyEngine(out var now);
        var target = new VisualKeyframePose(11.5, 2, 20, 25);
        var estimate = Pose(10, 2, 20, 20, 2.5);

        var decision = engine.Evaluate(
            estimate,
            LowConfirmed(target),
            SuperConfirmed(target),
            superKeyframesAvailable: true,
            now);

        Require(decision.Mode == VisualCorrectionMode.Medium,
            $"rattrapage moyen attendu, reçu {decision.Mode}");
        Require(decision.Source == VisualRelocalizationSource.Consensus,
            "le consensus low/high devrait être explicite");
        Require(decision.PositionGain is > 0.5 and < 1.0,
            "le rattrapage moyen ne doit pas snapper");
    }

    private static void RejectsCrossRecognizerDisagreement()
    {
        var engine = ReadyEngine(out var now);
        var low = new VisualKeyframePose(2, 0, 2, 0);
        var high = new VisualKeyframePose(12, 0, 2, 0);

        var decision = engine.Evaluate(
            Pose(2, 0, 2, 0, 5),
            LowConfirmed(low),
            SuperConfirmed(high),
            superKeyframesAvailable: true,
            now);

        Require(decision.Mode == VisualCorrectionMode.None,
            "deux reconnaissances incompatibles ne doivent jamais corriger");
        Require(engine.GetMetrics(now).RejectedCandidates == 1,
            "le rejet de sosie devrait être compté");
    }

    private static void StrongGlobalLoopClosureAllowsHardSnap()
    {
        var engine = ReadyEngine(out var now);
        var target = new VisualKeyframePose(25, 4, -10, 90);
        var estimate = Pose(0, 4, 0, 20, 12);

        var super = new SuperKeyframeRecognition(
            SuperKeyframeRecognitionState.Confirmed,
            9,
            target,
            5,
            CompositeDistance: 0.03,
            UniquenessMargin: 0.09,
            RegionalConsensus: 0.96,
            ComparedBits: 1800,
            GlobalLoopClosure: true);

        var decision = engine.Evaluate(
            estimate,
            VisualKeyframeRecognition.NoMatch(),
            super,
            superKeyframesAvailable: true,
            now);

        Require(decision.Mode == VisualCorrectionMode.HardSnap,
            $"loop closure fort devrait autoriser snap, reçu {decision.Mode}");
        Require(decision.GlobalLoopClosure,
            "le loop closure doit être marqué");
        var metrics = engine.GetMetrics(now);
        Require(metrics.HardSnaps == 1 && metrics.LoopClosures == 1,
            "métriques de loop closure incorrectes");
    }

    private static VisualRelocalizationEngineV2 ReadyEngine(out long now)
    {
        var engine = new VisualRelocalizationEngineV2();
        var start = Stopwatch.Frequency;
        engine.BeginSession(start);
        engine.NoteAbsoluteAnchor(start);
        now = start + Seconds(2);
        return engine;
    }

    private static RealtimePose Pose(
        double x,
        double y,
        double z,
        double yaw,
        double uncertainty) =>
        new(x, y, z, yaw, uncertainty, 2.0);

    private static VisualKeyframeRecognition LowConfirmed(
        VisualKeyframePose pose) => new(
            VisualKeyframeRecognitionState.Confirmed,
            1,
            pose,
            4,
            DescriptorDistance: 0.035,
            UniquenessMargin: 0.09,
            ComparedBits: 600);

    private static SuperKeyframeRecognition SuperConfirmed(
        VisualKeyframePose pose) => new(
            SuperKeyframeRecognitionState.Confirmed,
            2,
            pose,
            4,
            CompositeDistance: 0.06,
            UniquenessMargin: 0.07,
            RegionalConsensus: 0.88,
            ComparedBits: 1600,
            GlobalLoopClosure: false);

    private static long Seconds(double seconds) =>
        (long)Math.Round(seconds * Stopwatch.Frequency);

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
}