using System.Windows.Forms;

namespace RaidPilot.Overlay.Tests;

internal static class RawKeyboardTimelineTests
{
    internal const int TestCount = 6;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("tap clavier bref mesuré entre deux ticks", MeasuresQuickTap, failures);
        Run("maintien clavier couvre tout l'intervalle", MeasuresHeldKey, failures);
        Run("répétition automatique ignorée", IgnoresAutoRepeat, failures);
        Run("touches simultanées restent indépendantes", KeepsKeysIndependent, failures);
        Run("historique clavier borné et monotone", BoundsAndOrdersHistory, failures);
        Run("changement de premier plan remet la chronologie à zéro", ResetsTimeline, failures);
    }

    private static void MeasuresQuickTap()
    {
        var timeline = new RawKeyboardTimeline();
        timeline.Synchronize(Keys.W, isDown: false);
        timeline.Record(Keys.W, isDown: true, timestamp: 110);
        timeline.Record(Keys.W, isDown: false, timestamp: 120);

        Require(timeline.TrySample(Keys.W, 100, 150, out var sample), "échantillon absent");
        Require(!sample.IsDown, "la touche devrait être relâchée en fin d'intervalle");
        Require(sample.WasPressed && sample.WasReleased, "les deux arêtes du tap devraient être conservées");
        RequireNear(sample.HeldFraction, 0.2, 1e-12, "durée du tap incorrecte");
    }

    private static void MeasuresHeldKey()
    {
        var timeline = new RawKeyboardTimeline();
        timeline.Synchronize(Keys.W, isDown: true);

        Require(timeline.TrySample(Keys.W, 200, 260, out var sample), "échantillon absent");
        Require(sample.IsDown, "la touche devrait rester maintenue");
        RequireNear(sample.HeldFraction, 1.0, 1e-12, "un maintien complet devrait valoir 100 %");
    }

    private static void IgnoresAutoRepeat()
    {
        var timeline = new RawKeyboardTimeline();
        timeline.Synchronize(Keys.X, isDown: false);
        timeline.Record(Keys.X, isDown: true, timestamp: 10);
        timeline.Record(Keys.X, isDown: true, timestamp: 20);

        Require(timeline.Count == 1, "un autorepeat ne devrait pas ajouter une deuxième arête");
        Require(timeline.TrySample(Keys.X, 0, 30, out var sample), "échantillon absent");
        Require(sample.WasPressed && sample.IsDown, "l'appui initial devrait rester visible");
    }

    private static void KeepsKeysIndependent()
    {
        var timeline = new RawKeyboardTimeline();
        timeline.Synchronize(Keys.W, isDown: false);
        timeline.Synchronize(Keys.D, isDown: false);
        timeline.Record(Keys.W, isDown: true, timestamp: 10);
        timeline.Record(Keys.D, isDown: true, timestamp: 20);
        timeline.Record(Keys.W, isDown: false, timestamp: 30);

        Require(timeline.TrySample(Keys.W, 0, 40, out var forward), "W absent");
        Require(timeline.TrySample(Keys.D, 0, 40, out var right), "D absent");
        RequireNear(forward.HeldFraction, 0.5, 1e-12, "durée W incorrecte");
        RequireNear(right.HeldFraction, 0.5, 1e-12, "durée D incorrecte");
        Require(!forward.IsDown && right.IsDown, "les états finaux des touches sont mélangés");
    }

    private static void BoundsAndOrdersHistory()
    {
        var timeline = new RawKeyboardTimeline(capacity: 2);
        timeline.Synchronize(Keys.W, isDown: false);
        timeline.Record(Keys.W, isDown: true, timestamp: 10);
        timeline.Record(Keys.W, isDown: false, timestamp: 20);
        timeline.Record(Keys.W, isDown: true, timestamp: 15); // borné à 20

        Require(timeline.Count == timeline.Capacity, "le tampon devrait rester borné");
        Require(!timeline.TrySample(Keys.W, 0, 30, out _), "un intervalle antérieur à l'éviction est incomplet");
        Require(timeline.TrySample(Keys.W, 10, 30, out var recent), "l'intervalle récent devrait rester calculable");
        Require(recent.WasPressed && recent.WasReleased && recent.IsDown, "l'ordre monotone des arêtes est incorrect");
    }

    private static void ResetsTimeline()
    {
        var timeline = new RawKeyboardTimeline();
        timeline.Synchronize(Keys.W, isDown: false);
        timeline.Record(Keys.W, isDown: true, timestamp: 10);
        timeline.Reset();

        Require(timeline.Count == 0, "les arêtes devraient être effacées");
        Require(!timeline.TrySample(Keys.W, 10, 20, out _), "un ancien état ne devrait pas franchir le changement de focus");
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

    private static void RequireNear(double actual, double expected, double tolerance, string message)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException($"{message} ({actual} au lieu de {expected})");
        }
    }
}
