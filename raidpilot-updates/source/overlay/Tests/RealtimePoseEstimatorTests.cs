namespace RaidPilot.Overlay.Tests;

internal static class RealtimePoseEstimatorTests
{
    internal const int TestCount = 20;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("ancrage de la pose temps réel", AnchorsFromScreenshot, failures);
        Run("déplacement avant dans le repère Unity", MovesForwardUsingUnityYaw, failures);
        Run("déplacement latéral sans vitesse diagonale gratuite", MovesRightAndNormalizesDiagonal, failures);
        Run("vitesses marche sprint et lente", UsesMovementSpeeds, failures);
        Run("sensibilités souris hip et ADS", UsesHipAndAdsSensitivity, failures);
        Run("correction visuelle du cap sans déplacement", AppliesVisualYawCorrectionWithoutMoving, failures);
        Run("intégration au milieu d'un virage", IntegratesAtTurnMidpoint, failures);
        Run("croissance puis recalage de l'incertitude", GrowsAndResetsUncertainty, failures);
        Run("10 secondes bloqué contre un mur", CoversTenSecondsBlockedAgainstWall, failures);
        Run("vitesses arrière et latérale réalistes", AppliesDirectionalSpeedMultipliers, failures);
        Run("vitesse graduelle de la molette", AppliesMovementSpeedScale, failures);
        Run("accélération et freinage progressifs", AcceleratesAndDecelerates, failures);
        Run("mouvement visuel garde une incertitude prudente", MovingEvidenceStaysAsUncertainAsUnknown, failures);
        Run("immobilité visuelle bloque la translation", StopsOnConfirmedStationaryEvidence, failures);
        Run("ZUPT visuel réduit la propagation sans effacer l'historique", StationaryZuptReducesFutureDrift, failures);
        Run("recalage historique conserve le déplacement", HistoricalReanchorPreservesPostCaptureMovement, failures);
        Run("recalage historique corrige le cap et la vélocité", HistoricalReanchorRotatesHeadingAndVelocity, failures);
        Run("recalage historique conserve seulement l'incertitude récente", HistoricalReanchorKeepsOnlyPostCaptureUncertainty, failures);
        Run("nouveaux paramètres conservent l'état dynamique", ApplyingSettingsPreservesDynamicState, failures);
        Run("rejet des entrées invalides", RejectsInvalidInputs, failures);
    }

    private static void AnchorsFromScreenshot()
    {
        var estimator = NewEstimator();
        var pose = estimator.Anchor(Observation(12, 3, -7, 370));

        Near(pose.X, 12, "x");
        Near(pose.Y, 3, "y");
        Near(pose.Z, -7, "z");
        Near(pose.YawDegrees, 10, "yaw normalisé");
        Near(pose.PositionUncertaintyMeters, 0.25, "incertitude de position initiale");
        Near(pose.YawUncertaintyDegrees, 0.5, "incertitude de cap initiale");
        Require(estimator.IsAnchored, "l'estimateur devrait être ancré");
    }

    private static void AppliesVisualYawCorrectionWithoutMoving()
    {
        var estimator = NewEstimator();
        estimator.Anchor(Observation(12, 3, -7, 40));
        var corrected = estimator.ApplyYawCorrection(0.6);

        Near(corrected.X, 12, "x inchangé");
        Near(corrected.Y, 3, "y inchangé");
        Near(corrected.Z, -7, "z inchangé");
        Near(corrected.YawDegrees, 40.6, "yaw corrigé", 0.000001);
    }

    private static void MovesForwardUsingUnityYaw()
    {
        var estimator = NewEstimator();
        estimator.Anchor(Observation(0, 8, 0, 90));

        var pose = estimator.Advance(2, 1, 0, sprint: false, slow: false, 0, aiming: false);

        Near(pose.X, 4, "x vers +X");
        Near(pose.Y, 8, "y conservé");
        Near(pose.Z, 0, "z", 0.000001);
    }

    private static void MovesRightAndNormalizesDiagonal()
    {
        var lateral = NewEstimator();
        lateral.Anchor(Observation(0, 0, 0, 0));
        var lateralPose = lateral.Advance(1, 0, 1, sprint: false, slow: false, 0, aiming: false);
        Near(lateralPose.X, 2, "déplacement à droite");
        Near(lateralPose.Z, 0, "z latéral");

        var diagonal = NewEstimator();
        diagonal.Anchor(Observation(0, 0, 0, 0));
        var diagonalPose = diagonal.Advance(1, 1, 1, sprint: false, slow: false, 0, aiming: false);
        Near(Math.Sqrt((diagonalPose.X * diagonalPose.X) + (diagonalPose.Z * diagonalPose.Z)), 2,
            "distance diagonale");
    }

    private static void UsesMovementSpeeds()
    {
        var sprint = NewEstimator();
        sprint.Anchor(Observation(0, 0, 0, 0));
        Near(sprint.Advance(1, 1, 0, sprint: true, slow: false, 0, false).Z, 6, "sprint");

        var slow = NewEstimator();
        slow.Anchor(Observation(0, 0, 0, 0));
        Near(slow.Advance(1, 1, 0, sprint: true, slow: true, 0, false).Z, 1, "marche lente prioritaire");
    }

    private static void UsesHipAndAdsSensitivity()
    {
        var hip = NewEstimator();
        hip.Anchor(Observation(0, 0, 0, 350));
        Near(hip.Advance(0, 0, 0, false, false, 100, aiming: false).YawDegrees, 10, "yaw hip");

        var ads = NewEstimator();
        ads.Anchor(Observation(0, 0, 0, 350));
        Near(ads.Advance(0, 0, 0, false, false, 100, aiming: true).YawDegrees, 0, "yaw ADS");
    }

    private static void IntegratesAtTurnMidpoint()
    {
        var estimator = NewEstimator();
        estimator.Anchor(Observation(0, 0, 0, 0));

        var pose = estimator.Advance(1, 1, 0, false, false, 450, aiming: false);

        // 450 compteurs à 0,2 degré = 90 degrés; le déplacement est intégré à 45 degrés.
        Near(pose.X, Math.Sqrt(2), "x au milieu du virage", 0.000001);
        Near(pose.Z, Math.Sqrt(2), "z au milieu du virage", 0.000001);
        Near(pose.YawDegrees, 90, "yaw final");
    }

    private static void GrowsAndResetsUncertainty()
    {
        var estimator = NewEstimator();
        estimator.Anchor(Observation(0, 0, 0, 0));

        var extrapolated = estimator.Advance(2, 1, 0, true, false, 100, false);
        Require(extrapolated.PositionUncertaintyMeters > 0.25, "l'incertitude de position n'augmente pas");
        Require(extrapolated.YawUncertaintyDegrees > 0.5, "l'incertitude de cap n'augmente pas");

        var recalibrated = estimator.Anchor(Observation(20, 4, -3, -15));
        Near(recalibrated.PositionUncertaintyMeters, 0.25, "incertitude recalée");
        Near(recalibrated.YawUncertaintyDegrees, 0.5, "cap recalé");
        Near(recalibrated.YawDegrees, 345, "yaw négatif normalisé");
    }

    private static void CoversTenSecondsBlockedAgainstWall()
    {
        var estimator = NewEstimator();
        estimator.Anchor(Observation(0, 0, 0, 0));

        const double stepSeconds = 0.05;
        const int stepCount = 200;
        double? secondsBeforeRecalibrationThreshold = null;

        for (var step = 1; step <= stepCount; step++)
        {
            var pose = estimator.Advance(
                stepSeconds,
                forward: 1,
                right: 0,
                sprint: false,
                slow: false,
                mouseDeltaCounts: 0,
                aiming: false);

            if (secondsBeforeRecalibrationThreshold is null
                && pose.PositionUncertaintyMeters >= 8.0)
            {
                secondsBeforeRecalibrationThreshold = step * stepSeconds;
            }
        }

        var finalPose = estimator.CurrentPose!.Value;
        Require(
            secondsBeforeRecalibrationThreshold is > 0 and < 9.0,
            $"le seuil de recalage de 8 m devrait être atteint avant 9 s, reçu {secondsBeforeRecalibrationThreshold?.ToString("F2") ?? "jamais"} s");
        Require(
            finalPose.PositionUncertaintyMeters >= 8.0,
            $"l'incertitude {finalPose.PositionUncertaintyMeters:F2} m devrait clairement demander un recalage si le joueur est resté bloqué");
    }

    private static void AppliesDirectionalSpeedMultipliers()
    {
        var estimator = new RealtimePoseEstimator(new RealtimePoseEstimatorSettings
        {
            WalkSpeedMetersPerSecond = 2,
            BackwardSpeedMultiplier = 0.7,
            StrafeSpeedMultiplier = 0.8,
            AccelerationMetersPerSecondSquared = 10_000_000,
            DecelerationMetersPerSecondSquared = 10_000_000,
        });
        estimator.Anchor(Observation(0, 0, 0, 0));
        var backward = estimator.Advance(1, -1, 0, false, false, 0, false);
        Near(backward.Z, -1.4, "vitesse arrière");

        estimator.Anchor(Observation(0, 0, 0, 0));
        var strafe = estimator.Advance(1, 0, 1, false, false, 0, false);
        Near(strafe.X, 1.6, "vitesse latérale");
    }

    private static void AppliesMovementSpeedScale()
    {
        var estimator = NewEstimator();
        estimator.Anchor(Observation(0, 0, 0, 0));

        var pose = estimator.Advance(
            1,
            1,
            0,
            sprint: false,
            slow: false,
            mouseDeltaCounts: 0,
            aiming: false,
            movementSpeedScale: 0.5);

        Near(pose.Z, 1, "demi-vitesse");
    }

    private static void AcceleratesAndDecelerates()
    {
        var settings = new RealtimePoseEstimatorSettings
        {
            WalkSpeedMetersPerSecond = 4,
            BackwardSpeedMultiplier = 1,
            StrafeSpeedMultiplier = 1,
            AccelerationMetersPerSecondSquared = 2,
            DecelerationMetersPerSecondSquared = 4,
        };
        var estimator = new RealtimePoseEstimator(settings);
        estimator.Anchor(Observation(0, 0, 0, 0));

        var accelerating = estimator.Advance(1, 1, 0, false, false, 0, false);
        Near(accelerating.Z, 1, "première rampe d'accélération");

        var atCruiseSpeed = estimator.Advance(1, 1, 0, false, false, 0, false);
        Near(atCruiseSpeed.Z, 4, "seconde rampe d'accélération");

        var braking = estimator.Advance(1, 0, 0, false, false, 0, false);
        Near(braking.Z, 6, "distance de freinage");
        var stopped = estimator.Advance(1, 0, 0, false, false, 0, false);
        Near(stopped.Z, 6, "arrêt après freinage");

        var visuallyStopped = new RealtimePoseEstimator(settings);
        visuallyStopped.Anchor(Observation(0, 0, 0, 0));
        var beforeEvidence = visuallyStopped.Advance(1, 1, 0, false, false, 0, false);
        var stationary = visuallyStopped.Advance(
            1,
            1,
            0,
            false,
            false,
            0,
            false,
            motionEvidence: RealtimeMotionEvidence.Stationary);
        Near(stationary.Z, beforeEvidence.Z, "immobilité visuelle immédiate");
        var afterRelease = visuallyStopped.Advance(1, 0, 0, false, false, 0, false);
        Near(afterRelease.Z, beforeEvidence.Z, "élan annulé par l'immobilité visuelle");
    }

    private static void MovingEvidenceStaysAsUncertainAsUnknown()
    {
        var unknown = NewEstimator();
        unknown.Anchor(Observation(0, 0, 0, 0));
        var unknownPose = unknown.Advance(
            1,
            1,
            0,
            false,
            false,
            0,
            false,
            motionEvidence: RealtimeMotionEvidence.Unknown);

        var moving = NewEstimator();
        moving.Anchor(Observation(0, 0, 0, 0));
        var movingPose = moving.Advance(
            1,
            1,
            0,
            false,
            false,
            0,
            false,
            motionEvidence: RealtimeMotionEvidence.Moving);

        Near(
            movingPose.PositionUncertaintyMeters,
            unknownPose.PositionUncertaintyMeters,
            "incertitude avec image mobile");
    }

    private static void StopsOnConfirmedStationaryEvidence()
    {
        var estimator = NewEstimator();
        estimator.Anchor(Observation(0, 0, 0, 0));

        var pose = estimator.Advance(
            2,
            1,
            0,
            sprint: true,
            slow: false,
            mouseDeltaCounts: 0,
            aiming: false,
            movementSpeedScale: 1,
            motionEvidence: RealtimeMotionEvidence.Stationary);

        Near(pose.Z, 0, "translation stationnaire");
        Require(pose.PositionUncertaintyMeters < 1,
            "une immobilité confirmée ne devrait pas accumuler une erreur de déplacement importante");
    }

    private static void StationaryZuptReducesFutureDrift()
    {
        var unknown = NewEstimator();
        var stationary = NewEstimator();
        unknown.Anchor(Observation(0, 0, 0, 0));
        stationary.Anchor(Observation(0, 0, 0, 0));

        var unknownPose = unknown.Advance(
            10,
            0,
            0,
            sprint: false,
            slow: false,
            0,
            aiming: false,
            1.0,
            RealtimeMotionEvidence.Unknown);
        var stationaryPose = stationary.Advance(
            10,
            0,
            0,
            sprint: false,
            slow: false,
            0,
            aiming: false,
            1.0,
            RealtimeMotionEvidence.Stationary);

        Require(
            stationaryPose.PositionUncertaintyMeters
                < unknownPose.PositionUncertaintyMeters,
            "un arrêt confirmé devrait propager moins de dérive de position");
        Require(
            stationaryPose.YawUncertaintyDegrees
                < unknownPose.YawUncertaintyDegrees,
            "un arrêt confirmé devrait propager moins de dérive de cap");
        Require(
            stationaryPose.PositionUncertaintyMeters >= 0.25
                && stationaryPose.YawUncertaintyDegrees >= 0.5,
            "le ZUPT ne doit jamais effacer l'incertitude historique");
    }

    private static void HistoricalReanchorPreservesPostCaptureMovement()
    {
        var estimator = NewEstimator();
        var atCapture = estimator.Anchor(Observation(0, 2, 0, 0));
        var beforeCorrection = estimator.Advance(1, 1, 0, false, false, 0, false);

        var corrected = estimator.ReanchorFromHistoricalPose(
            Observation(10, 7, 20, 0),
            atCapture);

        Near(beforeCorrection.Z, 2, "déplacement avant le recalage");
        Near(corrected.X, 10, "x exact recalé");
        Near(corrected.Y, 7, "y exact recalé");
        Near(corrected.Z, 22, "déplacement post-capture conservé");
        Near(corrected.YawDegrees, 0, "cap conservé");
    }

    private static void HistoricalReanchorRotatesHeadingAndVelocity()
    {
        var settings = new RealtimePoseEstimatorSettings
        {
            WalkSpeedMetersPerSecond = 2,
            BackwardSpeedMultiplier = 1,
            StrafeSpeedMultiplier = 1,
            AccelerationMetersPerSecondSquared = 10_000_000,
            DecelerationMetersPerSecondSquared = 0,
            InitialPositionUncertaintyMeters = 0.25,
            InitialYawUncertaintyDegrees = 0.5,
        };
        var estimator = new RealtimePoseEstimator(settings);
        var atCapture = estimator.Anchor(Observation(0, 0, 0, 0));
        estimator.Advance(1, 1, 0, false, false, 0, false);

        var corrected = estimator.ReanchorFromHistoricalPose(
            Observation(100, 4, 50, 90),
            atCapture);

        Near(corrected.X, 102, "déplacement rotaté vers +X", 0.000001);
        Near(corrected.Z, 50, "déplacement retiré de +Z", 0.000001);
        Near(corrected.YawDegrees, 90, "cap corrigé");

        var coasting = estimator.Advance(1, 0, 0, false, false, 0, false);
        Near(coasting.X, 104, "vélocité rotatée vers +X", 0.000001);
        Near(coasting.Z, 50, "vélocité Z rotatée", 0.000001);
    }

    private static void HistoricalReanchorKeepsOnlyPostCaptureUncertainty()
    {
        var estimator = NewEstimator();
        estimator.Anchor(Observation(0, 0, 0, 0));
        estimator.Advance(2, 1, 0, false, false, 50, false);
        var atCapture = estimator.CurrentPose!.Value;
        var current = estimator.Advance(1, 1, 0, false, false, 25, false);

        var corrected = estimator.ReanchorFromHistoricalPose(
            Observation(20, 3, -10, atCapture.YawDegrees),
            atCapture);

        Near(
            corrected.PositionUncertaintyMeters,
            estimator.Settings.InitialPositionUncertaintyMeters
                + current.PositionUncertaintyMeters
                - atCapture.PositionUncertaintyMeters,
            "incertitude de position post-capture",
            0.000001);
        Near(
            corrected.YawUncertaintyDegrees,
            estimator.Settings.InitialYawUncertaintyDegrees
                + current.YawUncertaintyDegrees
                - atCapture.YawUncertaintyDegrees,
            "incertitude de cap post-capture",
            0.000001);
    }

    private static void ApplyingSettingsPreservesDynamicState()
    {
        var initialSettings = new RealtimePoseEstimatorSettings
        {
            WalkSpeedMetersPerSecond = 4,
            BackwardSpeedMultiplier = 1,
            StrafeSpeedMultiplier = 1,
            AccelerationMetersPerSecondSquared = 2,
            DecelerationMetersPerSecondSquared = 0,
        };
        var estimator = new RealtimePoseEstimator(initialSettings);
        estimator.Anchor(Observation(0, 0, 0, 0));
        var beforeSettings = estimator.Advance(1, 1, 0, false, false, 0, false);

        var calibratedSettings = initialSettings with
        {
            HipYawDegreesPerMouseCount = 0.123,
            WalkSpeedMetersPerSecond = 5,
        };
        estimator.ApplySettings(calibratedSettings);

        Require(estimator.CurrentPose == beforeSettings, "la pose a changé pendant l'application des paramètres");
        Require(ReferenceEquals(estimator.Settings, calibratedSettings), "les nouveaux paramètres ne sont pas actifs");

        var coasting = estimator.Advance(1, 0, 0, false, false, 0, false);
        Near(coasting.Z, 3, "vélocité conservée après calibration");
    }

    private static void RejectsInvalidInputs()
    {
        var estimator = NewEstimator();
        RequireThrows<InvalidOperationException>(() => estimator.Advance(1, 1, 0, false, false, 0, false),
            "un pas sans ancre devrait échouer");

        estimator.Anchor(Observation(0, 0, 0, 0));
        var before = estimator.CurrentPose;
        RequireThrows<ArgumentOutOfRangeException>(() => estimator.Advance(-1, 1, 0, false, false, 0, false),
            "un temps négatif devrait échouer");
        RequireThrows<ArgumentOutOfRangeException>(() => estimator.Advance(1, 1.1, 0, false, false, 0, false),
            "un axe hors plage devrait échouer");
        Require(estimator.CurrentPose == before, "une entrée invalide a modifié la pose");
    }

    private static RealtimePoseEstimator NewEstimator()
    {
        return new RealtimePoseEstimator(new RealtimePoseEstimatorSettings
        {
            HipYawDegreesPerMouseCount = 0.2,
            AdsYawDegreesPerMouseCount = 0.1,
            WalkSpeedMetersPerSecond = 2,
            SprintSpeedMetersPerSecond = 6,
            SlowSpeedMetersPerSecond = 1,
            BackwardSpeedMultiplier = 1,
            StrafeSpeedMultiplier = 1,
            AccelerationMetersPerSecondSquared = 10_000_000,
            DecelerationMetersPerSecondSquared = 10_000_000,
            InitialPositionUncertaintyMeters = 0.25,
            InitialYawUncertaintyDegrees = 0.5,
            PositionDriftMetersPerSecond = 0.02,
            DistanceErrorFraction = 0.05,
            YawDriftDegreesPerSecond = 0.05,
            MouseYawErrorFraction = 0.03
        });
    }

    private static ScreenshotObservation Observation(double x, double y, double z, double yaw)
    {
        return new ScreenshotObservation("anchor.png", x, y, z, 0, 0, 0, 1, yaw);
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