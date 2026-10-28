using System.Text.Json;

namespace RaidPilot.Overlay.Tests;

internal static class QuestContextTests
{
    internal const int TestCount = 29;
    private static readonly IReadOnlySet<string> Recognized = new HashSet<string> { "q1" };
    private static RealtimePose Pose(double x = 0, double y = 2, double z = 0, double uncertainty = 1) => new(x, y, z, 0, uncertainty, 1);
    private static QuestCandidate Quest(double x = 10, string map = "customs", string id = "q1", bool optional = false,
        double y = 2, IReadOnlyList<QuestContextPoint>? outline = null, bool possible = true) =>
        new(id, "Quête de test", "test", new[] { "Test" }, 1)
        {
            Objectives = new[] { new QuestContextObjective("o1", "findQuestItem", "Récupérez le document dans le bureau.", "fr", optional,
                "Clé requise", new[] { new QuestContextLocation("l1", map, new(x, y, 0), outline ?? Array.Empty<QuestContextPoint>(), possible, "") }) }
        };
    private static QuestContextHint? Select(QuestCandidate q, RealtimePose? p = null, IReadOnlySet<string>? recognized = null) =>
        new QuestContextAdvisor().Select(new[] { q }, recognized ?? Recognized, "customs", 1, p ?? Pose(), 0.1, true);
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Assertion du contexte échouée"); }

    internal static void RunAll(ICollection<string> failures)
    {
        Run("contexte proche d'une quête reconnue", () => Require(Select(Quest())?.QuestId == "q1"), failures);
        Run("contexte refuse quête non reconnue", () => Require(Select(Quest(id: "q2")) is null), failures);
        Run("contexte refuse une autre carte", () => Require(Select(Quest(map: "woods")) is null), failures);
        Run("contexte masque les objectifs éloignés", () => Require(Select(Quest(x: 90)) is null), failures);
        Run("contexte refuse position trop incertaine", () => Require(Select(Quest(), Pose(uncertainty: 9)) is null), failures);
        Run("contexte refuse données numériques invalides", () => Require(Select(Quest(), Pose(x: double.NaN)) is null), failures);
        Run("contexte refuse une pose périmée", () => Require(new QuestContextAdvisor().Select(new[] { Quest() }, Recognized, "customs", 1, Pose(), 3, true) is null), failures);
        Run("contexte attend la première position", () => Require(new QuestContextAdvisor().Select(new[] { Quest() }, Recognized, "customs", 1, null, 0, true) is null), failures);
        Run("contexte inactif sans contrôles de jeu", () => Require(new QuestContextAdvisor().Select(new[] { Quest() }, Recognized, "customs", 1, Pose(), 0, false) is null), failures);
        Run("contexte utilise le contour de la zone", () => {
            QuestContextPoint[] polygon = [new(-10, 2, -10), new(10, 2, -10), new(10, 2, 10), new(-10, 2, 10)];
            Require(Select(Quest(x: 200, outline: polygon, possible: false))?.Distance == 0);
        }, failures);
        Run("contexte hystérésis de sortie", () => {
            var a = new QuestContextAdvisor(); var quests = new[] { Quest(x: 0) };
            Require(a.Select(quests, Recognized, "customs", 1, Pose(x: 25), 0, true) is not null);
            Require(a.Select(quests, Recognized, "customs", 1, Pose(x: 35), 0, true) is not null);
            Require(a.Select(quests, Recognized, "customs", 1, Pose(x: 55), 0, true) is null);
        }, failures);
        Run("contexte remet l'hystérésis à zéro au raid suivant", () => {
            var a = new QuestContextAdvisor(); var quests = new[] { Quest(x: 0) };
            Require(a.Select(quests, Recognized, "customs", 1, Pose(x: 25), 0, true) is not null);
            Require(a.Select(quests, Recognized, "customs", 2, Pose(x: 35), 0, true) is null);
        }, failures);
        Run("contexte ne certifie jamais l'étage", () => Require(Select(Quest(y: 7))!.Where.Contains("niveau à vérifier")), failures);
        Run("contexte refuse zone située sur un étage incompatible", () =>
            Require(Select(Quest(y: 12)) is null), failures);
        Run("contexte strictement local, pas une quête lointaine", () =>
            Require(Select(Quest(x: 40)) is null), failures);
        Run("contexte signale un objectif facultatif", () => Require(Select(Quest(optional: true))!.Note.Contains("Facultatif")), failures);
        Run("contexte ne certifie jamais la progression", () => Require(Select(Quest())!.Note.Contains("Progression non vérifiée")), failures);
        Run("contexte garde les emplacements possibles", () => Require(Select(Quest())!.Where.Contains("Emplacement possible")), failures);
        Run("contexte compte les objectifs simultanés", () => {
            var ids = new HashSet<string> { "q1", "q2" };
            Require(new QuestContextAdvisor().Select(new[] { Quest(), Quest(x: 12, id: "q2") }, ids, "customs", 1, Pose(), 0, true)?.NearbyCount == 2);
        }, failures);
        Run("contexte traduit les instructions françaises", () => Require(ParseObjective("description", "Récupérez le document.", "Find the document.").Single().Instruction == "Récupérez le document."), failures);
        Run("contexte conserve un repli anglais explicite", () => Require(ParseObjective("description", null, "Find the document.").Single().Language == "en"), failures);
        Run("contexte refuse une clé de traduction non résolue", () => Require(ParseObjective("5968ec9986f7741ddd6c1012", null, null).Count == 0), failures);
        Run("contexte conserve les conditions de clé", () => Require(ParseObjective("description", "Récupérez le document.", null).Single().Requirements.Contains("Clé requise")), failures);
        Run("contexte nettoie le HTML des descriptions", () => Require(ParseObjective("description", "<b>Récupérez</b> le &lt;document&gt;.", null).Single().Instruction == "Récupérez le ."), failures);
        Run("contexte rejette la position incomplète", () => {
            using var json = JsonDocument.Parse("[{\"id\":\"o\",\"description\":\"Trouvez le document\",\"zones\":[{\"map\":\"m\",\"position\":{\"x\":3}}]}]");
            Require(QuestContextData.Parse(json.RootElement, new HashSet<string>{"m"}, "customs", new Dictionary<string,string>(), new Dictionary<string,string>()).Count == 0);
        }, failures);
        Run("contexte conserve la carte et l’en-tête à 96 et 144 DPI", () => {
            foreach(var dpi in new[] {96, 144}) {
                var plain=OverlayLayoutCalculator.CalculateBounds(new Rectangle(0,0,1920,1080),dpi);
                var expanded=OverlayLayoutCalculator.CalculateBounds(new Rectangle(0,0,1920,1080),dpi,true);
                var footer=96*dpi/96;
                Require(expanded.Top==plain.Top && expanded.X==plain.X && expanded.Width==plain.Width
                    && expanded.Bottom==plain.Bottom+footer && expanded.Height==plain.Height+footer);
            }
        }, failures);
        Run("contexte DOM évite injection et superposition", () => {
            var js=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"InjectedBehavior.js"));
            var css=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"InjectedStyles.css"));
            Require(js.Contains("__raidPilotShowQuestContext") && js.Contains("context.sequence < questContextSequence")
                && js.Contains("node.textContent = text") && css.Contains("--raidpilot-quest-height")
                && css.Contains("#raidpilot-quest-context[hidden]") && js.Contains("var(--raidpilot-quest-height, 0px)"));
        }, failures);
        Run("contexte refuse une géométrie source contradictoire", () => {
            using var json = JsonDocument.Parse("[{\"id\":\"o\",\"description\":\"Localisez le bunker\",\"zones\":[{\"map\":\"m\",\"position\":{\"x\":400,\"y\":2,\"z\":50},\"outline\":[{\"x\":880,\"z\":120},{\"x\":900,\"z\":120},{\"x\":900,\"z\":140}]}]}]");
            Require(QuestContextData.Parse(json.RootElement, new HashSet<string>{"m"}, "woods", new Dictionary<string,string>(), new Dictionary<string,string>()).Count == 0);
        }, failures);
        Run("contexte retire la quête absente de la sélection", () => {
            var advisor=new QuestContextAdvisor(); var quests=new[]{Quest()};
            Require(advisor.Select(quests,Recognized,"customs",1,Pose(),0,true) is not null);
            Require(advisor.Select(quests,new HashSet<string>(),"customs",1,Pose(),0,true) is null);
        }, failures);
    }
    private static IReadOnlyList<QuestContextObjective> ParseObjective(string key,string? fr,string? en)
    {
        var json=JsonSerializer.Serialize(new[]{new{id="o",description=key,type="findQuestItem",requiredKeys=new[]{new[]{"key-id"}},
            possibleLocations=new[]{new{map="m",positions=new[]{new{x=10,y=2,z=0}}}}}});
        using var document=JsonDocument.Parse(json);
        return QuestContextData.Parse(document.RootElement,new HashSet<string>{"m"},"customs",
            fr is null?new Dictionary<string,string>():new Dictionary<string,string>{{key,fr}},
            en is null?new Dictionary<string,string>():new Dictionary<string,string>{{key,en}});
    }
    private static void Run(string name, Action action, ICollection<string> failures)
    {
        try { action(); Console.WriteLine("OK  " + name); }
        catch(Exception ex) { failures.Add(name+" — "+ex.Message); }
    }
}
