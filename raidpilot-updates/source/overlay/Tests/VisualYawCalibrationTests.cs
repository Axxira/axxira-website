namespace RaidPilot.Overlay.Tests;

internal static class VisualYawCalibrationTests
{
    internal const int TestCount = 8;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("yaw visuel: apprend après consensus", LearnsAfterConsensus, failures);
        Run("yaw visuel: déplacement interdit", RejectsMovement, failures);
        Run("yaw visuel: ADS interdit", RejectsAds, failures);
        Run("yaw visuel: free-look interdit", RejectsFreeLook, failures);
        Run("yaw visuel: flux faible rejeté", RejectsWeakFlow, failures);
        Run("yaw visuel: rotation annulée rejetée", RejectsCancellingMouseMotion, failures);
        Run("yaw visuel: correction temps réel bornée", ProducesBoundedRealtimeCorrection, failures);
        Run("yaw visuel: correction temps réel refuse mouvement", RealtimeCorrectionRejectsMovement, failures);
    }

    private static VisualEgomotionEstimate Good(double yaw = 5.0) => new(
        VisualEgomotionState.Tracking,
        SceneShiftX: -4,
        SceneShiftY: 0.2,
        VisualHorizontalDirection.Right,
        EstimatedYawDeltaDegrees: yaw,
        Confidence: 0.92,
        TexturedFeatureCount: 25,
        MatchedFeatureCount: 20,
        InlierFeatureCount: 18,
        InlierRatio: 0.90,
        ResidualPixels: 0.20,
        BrightnessDelta: 0);

    private static void LearnsAfterConsensus()
    {
        var calibration = new VisualYawCalibration();
        double? update = null;
        for (var i = 0; i < 5; i++)
            update = calibration.Observe(Good(), 100, 100, false, false, false, 0.04) ?? update;
        Require(update is > 0.04 and <= 0.0402 + 1e-9,
            $"une correction petite et bornée était attendue, reçu {update}");
        Require(calibration.AppliedUpdates == 1, "une seule mise à jour devait être appliquée");
    }

    private static void RejectsMovement()
    {
        var calibration = new VisualYawCalibration();
        Require(calibration.Observe(Good(), 100, 100, true, false, false, 0.05) is null,
            "un déplacement ne doit pas calibrer le yaw");
    }

    private static void RejectsAds()
    {
        var calibration = new VisualYawCalibration();
        Require(calibration.Observe(Good(), 100, 100, false, true, false, 0.05) is null,
            "ADS modifie l'optique et doit être exclu");
    }

    private static void RejectsFreeLook()
    {
        var calibration = new VisualYawCalibration();
        Require(calibration.Observe(Good(), 100, 100, false, false, true, 0.05) is null,
            "le free-look ne doit pas calibrer le corps");
    }

    private static void RejectsWeakFlow()
    {
        var calibration = new VisualYawCalibration();
        var weak = Good() with { Confidence = 0.40 };
        Require(calibration.Observe(weak, 100, 100, false, false, false, 0.05) is null,
            "un flux peu fiable doit être rejeté");
    }

    private static void RejectsCancellingMouseMotion()
    {
        var calibration = new VisualYawCalibration();
        Require(calibration.Observe(Good(), 10, 100, false, false, false, 0.05) is null,
            "un aller-retour souris ne doit pas devenir une calibration");
        Require(calibration.RejectedSamples >= 1, "le rejet statistique devrait être comptabilisé");
    }

    private static void ProducesBoundedRealtimeCorrection()
    {
        var calibration = new VisualYawCalibration();
        var correction = calibration.TryGetRealtimeCorrection(
            Good(yaw: 5.0),
            mouseCounts: 100,
            mouseMagnitude: 100,
            movementActive: false,
            aiming: false,
            freeLook: false,
            currentSensitivity: 0.04);
        Require(correction is > 0 and <= 0.75,
            $"correction visuelle temps réel attendue, reçu {correction}");
    }

    private static void RealtimeCorrectionRejectsMovement()
    {
        var calibration = new VisualYawCalibration();
        Require(calibration.TryGetRealtimeCorrection(
            Good(yaw: 5.0),
            100,
            100,
            movementActive: true,
            aiming: false,
            freeLook: false,
            currentSensitivity: 0.04) is null,
            "la vision ne doit pas corriger le yaw pendant une translation");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Run(string name, Action test, ICollection<string> failures)
    {
        try { test(); Console.WriteLine("OK  " + name); }
        catch (Exception ex)
        {
            failures.Add(name + " — " + ex.Message);
            Console.Error.WriteLine("ERR " + name + ": " + ex.Message);
        }
    }
}