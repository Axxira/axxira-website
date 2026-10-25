namespace RaidPilot.Overlay.Tests;

internal static class StashPilotTests
{
    internal const int TestCount = 8;
    internal static void RunAll(ICollection<string> failures)
    {
        Run("StashPilot OCR rejette le bruit et les répétitions", () =>
        {
            var rows = StashPilotImageScanner.FilterCandidates(
                [" Boulons ", "boulons", "", "X", new string('A', 181), "Ruban isolant"]);
            Require(rows.Count == 2 && rows[0] == "Boulons" && rows[1] == "Ruban isolant");
        }, failures);
        Run("StashPilot OCR ne fabrique pas de quantités confirmées", () =>
        {
            var rows = StashPilotImageScanner.FilterCandidates(["Boulons x4"]);
            Require(rows.Count == 1);
            Require(StashPilotAdvisor.ParseInventory("# OCR : " + rows[0]).Count == 0);
        }, failures);
        Run("StashPilot additionne besoins quêtes et planque", () =>
        {
            var a = StashPilotAdvisor.Analyze(
                [new("Boulons", 9, false, true)],
                [new("Boulons", 3, "Quête", false, true),
                 new("Boulons", 4, "Planque", false, true)]);
            Require(a.Count == 1 && a[0].Required == 7
                && a[0].ToKeep == 7 && a[0].Excess == 2);
        }, failures);
        Run("StashPilot n'étiquette jamais un objet sans objectif comme inutile", () =>
        {
            var a = StashPilotAdvisor.Analyze([new("Objet rare", 1, false, true)], []);
            Require(a[0].Recommendation == "Utilité non confirmée"
                && !a[0].VerifiedNeed);
        }, failures);
        Run("StashPilot refuse de certifier objet OCR non confirmé", () =>
        {
            var a = StashPilotAdvisor.Analyze([new("Boulons", 5, false, false)],
                [new("Boulons", 3, "Quête", false, true)]);
            Require(a[0].Recommendation == "À vérifier");
        }, failures);
        Run("StashPilot sépare contraintes FIR et total", () =>
        {
            var a = StashPilotAdvisor.Analyze(
                [new("Ruban", 2, true, true), new("Ruban", 3, false, true)],
                [new("Ruban", 4, "Quête", true, true)]);
            Require(a.All(x => x.MissingFoundInRaid == 2)
                && a.All(x => x.Required == 4));
        }, failures);
        Run("StashPilot ignore objectif non confirmé", () =>
        {
            var a = StashPilotAdvisor.Analyze(
                [new("Boulons", 2, false, true)],
                [new("Boulons", 100, "Futur non vérifié", false, false)]);
            Require(a[0].Required == 0);
        }, failures);
        Run("StashPilot importe lignes et rejette quantités invalides", () =>
        {
            var i = StashPilotAdvisor.ParseInventory(
                "Boulons;4;FIR;CONFIRMÉ\nErreur;-8\n");
            var n = StashPilotAdvisor.ParseNeeds(
                "Boulons;2;Planque;;CONFIRMÉ\nMauvais;abc;Quête");
            Require(i.Count == 1 && i[0].FoundInRaid && i[0].Confirmed
                && n.Count == 1 && n[0].Confirmed);
        }, failures);
    }
    private static void Run(string title, Action work, ICollection<string> failures)
    {
        try { work(); Console.WriteLine("OK  " + title); }
        catch (Exception ex) { failures.Add(title + " : " + ex.Message); }
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("StashPilot failed");
    }
}
