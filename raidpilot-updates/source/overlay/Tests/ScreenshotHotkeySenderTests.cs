namespace RaidPilot.Overlay.Tests;

internal static class ScreenshotHotkeySenderTests
{
    internal const int TestCount = 7;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("recalage automatique accepte F8", AcceptsKeyboardShortcut, failures);
        Run("recalage automatique refuse une molette", RejectsAxisShortcut, failures);
        Run("recalage automatique refuse Impr. écran à remplacer", RejectsUnsafeBinding, failures);
        Run("recalage automatique refuse un bouton souris", RejectsMouseButton, failures);
        Run("F8 en marche accepte seulement un raccourci simple", AcceptsSimpleMovingF8, failures);
        Run("F8 en marche refuse un raccourci multi-touches", RejectsMovingModifierChord, failures);
        Run("F8 en marche refuse un raccourci qui change le mouvement", RejectsMovingGameplayKey, failures);
    }

    private static void AcceptsKeyboardShortcut()
    {
        var binding = new EftScreenshotBindingInfo(
            EftScreenshotBindingStatus.Ready,
            "F8",
            ["F8"],
            null,
            null);
        Require(ScreenshotHotkeySender.CanSend(binding), "F8 devrait pouvoir être envoyé");
    }

    private static void RejectsAxisShortcut()
    {
        var binding = new EftScreenshotBindingInfo(
            EftScreenshotBindingStatus.Ready,
            "Molette vers le haut",
            Array.Empty<string>(),
            "Mouse Scroll",
            true);
        Require(!ScreenshotHotkeySender.CanSend(binding), "un axe ne doit pas être simulé");
    }

    private static void RejectsUnsafeBinding()
    {
        var binding = new EftScreenshotBindingInfo(
            EftScreenshotBindingStatus.NeedsRebind,
            "Impr. écran · à remplacer",
            ["SysReq"],
            null,
            null);
        Require(!ScreenshotHotkeySender.CanSend(binding), "un raccourci non prêt doit être refusé");
    }

    private static void RejectsMouseButton()
    {
        var binding = new EftScreenshotBindingInfo(
            EftScreenshotBindingStatus.Ready,
            "Bouton souris",
            ["Mouse0"],
            null,
            null);
        Require(!ScreenshotHotkeySender.CanSend(binding), "un bouton souris ne doit pas être injecté comme un clavier");
    }

    private static void AcceptsSimpleMovingF8()
    {
        var binding = new EftScreenshotBindingInfo(
            EftScreenshotBindingStatus.Ready, "F8", ["F8"], null, null);
        Require(ScreenshotHotkeySender.CanSendWhileMoving(binding),
            "L'option F8 en mouvement doit fonctionner pour F8 seul.");
    }

    private static void RejectsMovingModifierChord()
    {
        var binding = new EftScreenshotBindingInfo(
            EftScreenshotBindingStatus.Ready, "Maj+F8",
            ["LeftShift", "F8"], null, null);
        Require(!ScreenshotHotkeySender.CanSendWhileMoving(binding),
            "Un combo risquerait de relâcher le sprint en mouvement.");
    }

    private static void RejectsMovingGameplayKey()
    {
        var binding = new EftScreenshotBindingInfo(
            EftScreenshotBindingStatus.Ready, "W", ["W"], null, null);
        Require(!ScreenshotHotkeySender.CanSendWhileMoving(binding),
            "La touche de marche ne doit jamais être simulée comme capture.");
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
