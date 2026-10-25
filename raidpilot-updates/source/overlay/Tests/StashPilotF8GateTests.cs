using System.Diagnostics;

namespace RaidPilot.Overlay.Tests;

internal static class StashPilotF8GateTests
{
    internal const int TestCount = 6;
    private static long At(int seconds) => Stopwatch.Frequency * (10L + seconds);

    internal static void RunAll(ICollection<string> failures)
    {
        Run("Scan F8 non armé n'accepte aucune capture", () =>
        {
            var gate = new StashPilotF8Gate();
            Require(!gate.TryConsume(At(1), false, true));
        }, failures);
        Run("Scan F8 armé accepte un F8 manuel une seule fois", () =>
        {
            var gate = new StashPilotF8Gate();
            gate.Arm(At(0));
            Require(gate.TryConsume(At(1), false, true));
            Require(!gate.TryConsume(At(2), false, true));
        }, failures);
        Run("Scan F8 ne prend pas le screenshot automatique du GPS", () =>
        {
            var gate = new StashPilotF8Gate();
            gate.Arm(At(0));
            Require(!gate.TryConsume(At(2), true, true));
            Require(gate.IsArmed);
            Require(gate.TryConsume(At(3), false, true));
        }, failures);
        Run("Scan F8 refuse un fichier non vérifié", () =>
        {
            var gate = new StashPilotF8Gate();
            gate.Arm(At(0));
            Require(!gate.TryConsume(At(1), false, false) && gate.IsArmed);
        }, failures);
        Run("Scan F8 expire après 90 secondes", () =>
        {
            var gate = new StashPilotF8Gate();
            gate.Arm(At(0));
            Require(!gate.TryConsume(At(91), false, true) && !gate.IsArmed);
        }, failures);
        Run("Scan F8 annulation invalide la capture", () =>
        {
            var gate = new StashPilotF8Gate();
            gate.Arm(At(0));
            gate.Cancel();
            Require(!gate.TryConsume(At(1), false, true));
        }, failures);
    }

    private static void Run(string title, Action action, ICollection<string> failures)
    {
        try { action(); Console.WriteLine("OK  " + title); }
        catch (Exception e) { failures.Add(title + " : " + e.Message); }
    }

    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("StashPilot F8 gate");
    }
}
