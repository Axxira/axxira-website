namespace RaidPilot.Overlay.Tests;

internal static class RealtimeControlStateTests
{
    internal const int TestCount = 8;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("tap bref conservé entre deux ticks", CapturesQuickToggleTap, failures);
        Run("maintien sans double bascule", DoesNotRetoggleHeldKey, failures);
        Run("contrôle continu suit l'état physique", ContinuousBindingMirrorsPhysicalState, failures);
        Run("sprint toggle refusé sans marche avant", RejectsSprintToggleWithoutForwardMovement, failures);
        Run("marche jamais bloquée par faux Stationary visuel", WalkingIgnoresAmbiguousStationaryEvidence, failures);
        Run("snap visuel exclu de la calibration de distance", ExcludesVisualSnapFromCalibrationDistance, failures);
        Run("capture retardée d'un ancien raid refusée", RejectsDelayedScreenshotFromPreviousSession, failures);
        Run("seule une capture automatique apprend un repère", LearnsOnlyAutomaticClearViewCapture, failures);
    }

    private static void CapturesQuickToggleTap()
    {
        var result = OverlayForm.UpdateActionState(
            current: false,
            isToggle: true,
            new OverlayForm.KeySample(IsDown: false, WasPressedSinceLastPoll: true),
            wasDown: false);
        Require(result, "un tap complet entre deux ticks devrait basculer l'action");
    }

    private static void DoesNotRetoggleHeldKey()
    {
        var result = OverlayForm.UpdateActionState(
            current: true,
            isToggle: true,
            new OverlayForm.KeySample(IsDown: true, WasPressedSinceLastPoll: false),
            wasDown: true);
        Require(result, "une touche maintenue ne devrait pas rebascule l'action");
    }

    private static void ContinuousBindingMirrorsPhysicalState()
    {
        var down = OverlayForm.UpdateActionState(
            current: false,
            isToggle: false,
            new OverlayForm.KeySample(IsDown: true, WasPressedSinceLastPoll: true),
            wasDown: false);
        var up = OverlayForm.UpdateActionState(
            current: true,
            isToggle: false,
            new OverlayForm.KeySample(IsDown: false, WasPressedSinceLastPoll: false),
            wasDown: true);
        Require(down && !up, "un bind Continuous devrait suivre exactement l'état physique");
    }

    private static void RejectsSprintToggleWithoutForwardMovement()
    {
        var result = OverlayForm.UpdateActionState(
            current: false,
            isToggle: true,
            new OverlayForm.KeySample(IsDown: true, WasPressedSinceLastPoll: true),
            wasDown: false,
            allowToggle: false);
        Require(!result, "le sprint ne devrait pas s'armer sans mouvement avant");
    }

    private static void WalkingIgnoresAmbiguousStationaryEvidence()
    {
        Require(
            OverlayForm.FilterAmbiguousStationaryEvidence(
                RealtimeMotionEvidence.Stationary,
                movementInputActive: true,
                sprint: false) == RealtimeMotionEvidence.Unknown,
            "une marche commandée ne doit jamais être gelée par une image faussement stationnaire");
        Require(
            OverlayForm.FilterAmbiguousStationaryEvidence(
                RealtimeMotionEvidence.Stationary,
                movementInputActive: true,
                sprint: true) == RealtimeMotionEvidence.Stationary,
            "le sprint doit conserver le garde collision Stationary");
        Require(
            OverlayForm.FilterAmbiguousStationaryEvidence(
                RealtimeMotionEvidence.Stationary,
                movementInputActive: false,
                sprint: false) == RealtimeMotionEvidence.Stationary,
            "un vrai repos doit conserver Stationary pour le ZUPT");
        Require(
            OverlayForm.FilterAmbiguousStationaryEvidence(
                RealtimeMotionEvidence.Moving,
                movementInputActive: true,
                sprint: false) == RealtimeMotionEvidence.Moving,
            "une preuve de mouvement ne doit pas être altérée");
    }

    private static void ExcludesVisualSnapFromCalibrationDistance()
    {
        Require(OverlayForm.ShouldAccumulateCalibrationDistance(visuallyReanchored: false),
            "un déplacement intégré normal doit rester calibrable");
        Require(!OverlayForm.ShouldAccumulateCalibrationDistance(visuallyReanchored: true),
            "la distance artificielle d'un snap visuel ne doit jamais être calibrée");
    }

    private static void RejectsDelayedScreenshotFromPreviousSession()
    {
        var sessionStart = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        Require(!OverlayForm.IsScreenshotTimestampFromCurrentSession(
                sessionStart - TimeSpan.FromSeconds(3),
                sessionStart),
            "une capture antérieure à la tolérance de session doit être refusée");
        Require(OverlayForm.IsScreenshotTimestampFromCurrentSession(
                sessionStart - TimeSpan.FromSeconds(1),
                sessionStart),
            "la petite imprécision d'horloge du fichier doit rester tolérée");
        Require(OverlayForm.IsScreenshotTimestampFromCurrentSession(
                sessionStart + TimeSpan.FromSeconds(1),
                sessionStart),
            "une capture du raid courant doit être acceptée");
    }

    private static void LearnsOnlyAutomaticClearViewCapture()
    {
        Require(OverlayForm.ShouldScheduleVisualKeyframeLearning(
                isAutomaticReanchor: true,
                hasFileTimestamp: true,
                hasCaptureTimestamp: true),
            "une capture automatique corrélée devrait pouvoir devenir un repère");
        Require(!OverlayForm.ShouldScheduleVisualKeyframeLearning(
                isAutomaticReanchor: false,
                hasFileTimestamp: true,
                hasCaptureTimestamp: true),
            "une capture manuelle O/TÂCHES ne doit jamais devenir un repère");
        Require(!OverlayForm.ShouldScheduleVisualKeyframeLearning(
                isAutomaticReanchor: true,
                hasFileTimestamp: false,
                hasCaptureTimestamp: true),
            "un fichier non corrélé ne doit pas devenir un repère");
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}