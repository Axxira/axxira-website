namespace RaidPilot.Overlay.Tests;

internal static class OverlayAutoVisibilityTests
{
    internal const int TestCount = 7;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("auto-hide: curseur EFT en raid détecte interface", DetectsRaidUiCursor, failures);
        Run("auto-hide: curseur hors raid ignoré", IgnoresCursorOutsideRaid, failures);
        Run("auto-hide: curseur autre application ignoré", IgnoresBackgroundCursor, failures);
        Run("auto-hide: fin de raid BEClient reconnue", DetectsBattleEyeRaidEnd, failures);
        Run("auto-hide: sortie partielle BattleEye refusée", RejectsPartialBattleEyeExit, failures);
        Run("boussole écran: grand format indépendant", UsesLargeStandaloneCompass, failures);
        Run("boussole écran: cardinaux mondiaux", UsesWorldCardinals, failures);
    }

    private static void DetectsRaidUiCursor()
    {
        Require(
            OverlayForm.ShouldTreatCursorAsLootUi(
                raidActive: true,
                tarkovForeground: true,
                cursorVisible: true),
            "le curseur EFT visible en raid doit masquer la map");
    }

    private static void IgnoresCursorOutsideRaid()
    {
        Require(
            !OverlayForm.ShouldTreatCursorAsLootUi(
                raidActive: false,
                tarkovForeground: true,
                cursorVisible: true),
            "un menu hors raid ne doit pas être classé loot actif");
    }

    private static void IgnoresBackgroundCursor()
    {
        Require(
            !OverlayForm.ShouldTreatCursorAsLootUi(
                raidActive: true,
                tarkovForeground: false,
                cursorVisible: true),
            "le curseur d'une autre application ne doit pas masquer RaidPilot");
    }

    private static void DetectsBattleEyeRaidEnd()
    {
        Require(
            EftMapLogWatcher.IsRaidEndedLine(
                "2026-10-21 14:03:04.692|Info|application|BEClient exit successfully"),
            "le marqueur réel de fin de raid EFT n'est pas reconnu");
    }

    private static void RejectsPartialBattleEyeExit()
    {
        Require(
            !EftMapLogWatcher.IsRaidEndedLine(
                "2026-10-21 14:03:04.591|Info|application|BEClient exit"),
            "la première ligne BEClient ne doit pas terminer le raid deux fois");
    }

    private static void UsesLargeStandaloneCompass()
    {
        Require(
            CompassOverlayForm.CompassWidthDip >= 850
            && CompassOverlayForm.CompassHeightDip >= 90,
            "la boussole plein écran doit être nettement plus grande que celle de la map");
    }

    private static void UsesWorldCardinals()
    {
        Require(
            CompassOverlayForm.CardinalLabel(0) == "N"
            && CompassOverlayForm.CardinalLabel(90) == "E"
            && CompassOverlayForm.CardinalLabel(180) == "S"
            && CompassOverlayForm.CardinalLabel(270) == "O"
            && Math.Abs(CompassOverlayForm.Normalize360(-10) - 350) < 0.0001
            && Math.Abs(CompassOverlayForm.AngularDistance(359, 1) - 2) < 0.0001
            && CompassOverlayForm.MinimumHeadingRepaintDegrees > 0,
            "les cardinaux ou la normalisation de cap sont incorrects");
    }

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
            failures.Add($"{name}: {exception.Message}");
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
