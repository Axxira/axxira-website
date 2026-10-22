namespace RaidPilot.Overlay.Tests;

internal static class TimerReliabilityTests
{
    internal const int TestCount = 16;
    private static readonly string[] Panel = ["RAID TIME 39:42", "EXFIL01 Crossroads", "EXFIL02 Trailer Park"];
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Régression du chrono"); }
    internal static void RunAll(ICollection<string> failures)
    {
        Run("chrono reste relisible après EXFIL complet", () => {
            var p = RaidPanelReadPlan.FromLines(Panel, true);
            Require(p.ReadTimer && !p.ReadQuests && !p.UpdateExtractions);
        }, failures);
        Run("chrono et sorties lors de la première lecture", () => {
            var p = RaidPanelReadPlan.FromLines(Panel, false);
            Require(p.ReadTimer && !p.ReadQuests && p.UpdateExtractions);
        }, failures);
        Run("écran tâches conserve sa voie de lecture", () => {
            var p = RaidPanelReadPlan.FromLines(["TASKS", "NAME LOCATION STATUS", "Search Mission"], true);
            Require(p.ReadQuests && !p.ReadTimer && !p.UpdateExtractions);
        }, failures);
        Run("panneau vide ne crée pas un chrono", () => Require(!RaidPanelReadPlan.FromLines([], false).ReadTimer), failures);
        Run("transit seul ne synchronise pas le chrono", () => Require(!RaidPanelReadPlan.FromLines(["TRANSIT01 Customs"], false).ReadTimer), failures);
        Run("un rang EXFIL suffit à autoriser la lecture du temps", () => Require(RaidPanelReadPlan.FromLines(["EXFIL01 Crossroads"], true).ReadTimer), failures);
        Run("premier chrono illisible récupérable sans réinitialiser EXFIL", () => {
            var c = new RaidCountdown(1000);var g = c.StartSession();
            Require(!RaidTimerParser.TryParseRemainingSeconds(["RAID TIME --:--"], out _));
            Require(!c.GetSnapshot(11000).IsSynchronized);
            var next = RaidPanelReadPlan.FromLines(Panel, true);
            Require(next.ReadTimer && !next.UpdateExtractions);
            Require(RaidTimerParser.TryParseRemainingSeconds(["RAID TIME 39:42"], out var seconds));
            Require(c.TrySynchronize(g, seconds, 20000, 21500));
            Require(Math.Abs(c.GetSnapshot(21500).RemainingSeconds - 2380.5) < 0.001);
        }, failures);
        Run("lecture du chrono indépendante d'un catalogue de sorties", () => {
            var p = RaidPanelReadPlan.FromLines(["EXFIL01 Nom encore inconnu"], false);
            Require(p.ReadTimer); // No network or extraction candidate parameter exists in this decision.
        }, failures);
        Run("une lecture ultérieure ratée conserve le temps sain", () => {
            var c = new RaidCountdown(1000);var g = c.StartSession();c.TrySynchronize(g,600,10000,10000);
            Require(!RaidTimerParser.TryParseRemainingSeconds(["illisible"],out _));
            Require(c.GetSnapshot(20000).RemainingSeconds==590);
        }, failures);
        Run("même capture ne décale pas l'horloge deux fois", () => {
            var c = new RaidCountdown(1000);var g = c.StartSession();Require(c.TrySynchronize(g,600,10000,10000));
            Require(!c.TrySynchronize(g,605,10000,11000));Require(c.GetSnapshot(11000).RemainingSeconds==599);
        }, failures);
        Run("deux temps OCR contradictoires sont refusés", () => Require(!RaidTimerParser.TryParseRemainingSeconds(["39:42","38:42"],out _)), failures);
        Run("deux en-têtes temporels contradictoires sont refusés", () => Require(!RaidTimerParser.TryParseRemainingSeconds(["RAID TIME 39:42","RAID TIME 39:12"],out _)), failures);
        Run("même temps lu deux fois reste admissible", () => {
            Require(RaidTimerParser.TryParseRemainingSeconds(["RAID TIME 39:42","RAID TIME 39:42"],out var s)&&s==2382);
        }, failures);
        Run("l'en-tête du raid prime sur un temps sans libellé", () => {
            Require(RaidTimerParser.TryParseRemainingSeconds(["05:00","RAID TIME 39:42"],out var s)&&s==2382);
        }, failures);
        Run("nouveau raid ne reprend pas l'ancienne mesure", () => {
            var c = new RaidCountdown(1000);var a=c.StartSession();c.TrySynchronize(a,600,10000,10000);var b=c.StartSession();
            Require(!c.TrySynchronize(a,580,30000,30000));Require(!c.GetSnapshot(30000).IsSynchronized&&b!=a);
        }, failures);
        Run("décompte récupère le temps écoulé après une longue pause UI", () => {
            var c = new RaidCountdown(1000);var g=c.StartSession();c.TrySynchronize(g,600,10000,10000);
            Require(c.GetSnapshot(130000).RemainingSeconds==480);
        }, failures);
    }
    private static void Run(string name,Action action,ICollection<string> failures)
    {
        try {action();Console.WriteLine("OK  "+name);}
        catch(Exception ex) {failures.Add(name+" — "+ex.Message);Console.Error.WriteLine("FAIL "+name);}
    }
}
