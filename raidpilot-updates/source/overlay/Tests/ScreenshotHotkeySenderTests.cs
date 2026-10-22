namespace RaidPilot.Overlay.Tests;

internal static class ScreenshotHotkeySenderTests
{
    internal const int TestCount = 4;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("recalage automatique accepte F8", AcceptsKeyboardShortcut, failures);
        Run("recalage automatique refuse une molette", RejectsAxisShortcut, failures);
        Run("recalage automatique refuse Impr. écran à remplacer", RejectsUnsafeBinding, failures);
        Run("recalage automatique refuse un bouton souris", RejectsMouseButton, failures);
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
