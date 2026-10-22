namespace RaidPilot.Overlay.Tests;

internal static class QuestAdvisorTests
{
    internal const int TestCount = 9;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("nom français de quête dans une rangée", ResolvesFrenchNameInsideRow, failures);
        Run("alias anglais de quête", ResolvesEnglishAlias, failures);
        Run("petite erreur OCR sur un titre long", ResolvesUniqueLongOcrError, failures);
        Run("titre long prioritaire sur son préfixe", PrefersCompleteLongTitle, failures);
        Run("égalité OCR de quêtes refusée", RejectsAmbiguousMatch, failures);
        Run("titre court approximatif refusé", RejectsFuzzyShortTitle, failures);
        Run("détection locale du mode PvE", DetectsPveMode, failures);
        Run("écran de quêtes confirmé par ses en-têtes", RecognizesQuestScreen, failures);
        Run("inventaire refusé comme écran de quêtes", RejectsInventoryScreen, failures);
    }

    private static void ResolvesFrenchNameInsideRow()
    {
        var resolved = QuestAdvisor.ResolveFromOcrLines(
            [Quest("q1", "Livraison du passé", "Delivery from the Past")],
            ["Prapor   Livraison du passé   Customs   En cours"]);

        Require(resolved.SetEquals(["q1"]), "le titre français devrait être reconnu");
    }

    private static void ResolvesEnglishAlias()
    {
        var resolved = QuestAdvisor.ResolveFromOcrLines(
            [Quest("q1", "Livraison du passé", "Delivery from the Past")],
            ["Delivery from the Past  1/2"]);

        Require(resolved.SetEquals(["q1"]), "l'alias anglais devrait être reconnu");
    }

    private static void ResolvesUniqueLongOcrError()
    {
        var resolved = QuestAdvisor.ResolveFromOcrLines(
            [
                Quest("q1", "Connaissance du territoire", "Acquaintance"),
                Quest("q2", "La voie du chasseur", "The Huntsman Path"),
            ],
            ["Connaissance du teritoire"]);

        Require(resolved.SetEquals(["q1"]), "une erreur OCR unique devrait être tolérée");
    }

    private static void PrefersCompleteLongTitle()
    {
        var resolved = QuestAdvisor.ResolveFromOcrLines(
            [
                Quest("q1", "Ça roule", "Wheels in Motion"),
                Quest("q2", "Ça roule - On essaie encore", "Wheels in Motion - Again"),
            ],
            ["Prapor  Ça roule - On essaie encore  Reserve  En cours  1/2"]);

        Require(resolved.SetEquals(["q2"]),
            "le titre complet devrait battre son préfixe de quête");
    }

    private static void RejectsAmbiguousMatch()
    {
        var resolved = QuestAdvisor.ResolveFromOcrLines(
            [
                Quest("q1", "Signal partie 1", "Signal Part 1"),
                Quest("q2", "Signal partie 1", "Signal Part 1"),
            ],
            ["Signal partie 1"]);

        Require(resolved.Count == 0, "un même titre associé à deux IDs doit être refusé");
    }

    private static void RejectsFuzzyShortTitle()
    {
        var resolved = QuestAdvisor.ResolveFromOcrLines(
            [Quest("q1", "Setup", "Setup")],
            ["Setvp"]);

        Require(resolved.Count == 0, "un titre court ne doit pas utiliser le flou");
    }

    private static void DetectsPveMode()
    {
        Require(QuestAdvisor.DetectGameMode(["1.2.5 LOCAL | PVE"]) == "pve",
            "le libellé PvE devrait sélectionner le catalogue PvE");
        Require(QuestAdvisor.DetectGameMode(["ESCAPE FROM TARKOV"]) == "regular",
            "le mode normal devrait rester le repli");
        Require(QuestAdvisor.DetectGameMode(["ESCAPE FROM TARKOV"], "pve") == "pve",
            "un filigrane PvE manquant ne doit pas changer de mode au milieu du raid");
    }

    private static void RecognizesQuestScreen()
    {
        Require(QuestAdvisor.IsQuestScreen(
            ["ACTIVE TASKS", "TRADER  NAME  LOCATION  STATUS  PROGRESS"]),
            "les en-têtes anglais du tableau devraient confirmer l'écran de quêtes");
        Require(QuestAdvisor.IsQuestScreen(
            ["TÂCHES ACTIVES", "MARCHAND  NOM  EMPLACEMENT  ÉTAT  PROGRESSION"]),
            "les en-têtes français du tableau devraient confirmer l'écran de quêtes");
    }

    private static void RejectsInventoryScreen()
    {
        Require(!QuestAdvisor.IsQuestScreen(
            [
                "ÉQMT. 9 COMPÉTENCE 9 CARTE e QUÉTES O SUCCÉS PRESTIGE",
                "GILET TACT.",
                "CASQUE ELEC. COUVRE-CHEF MASQUE",
                "POCHES EMPL. SPÉCIAUX",
            ]),
            "la barre d'onglets de l'inventaire ne prouve pas que la liste de quêtes est ouverte");
    }

    private static QuestCandidate Quest(string id, string french, string english)
    {
        return new QuestCandidate(id, french, french.ToLowerInvariant().Replace(' ', '-'),
            [french, english], 1);
    }

    private static void Run(string name, Action test, ICollection<string> failures)
    {
        try
        {
            test();
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
