namespace RaidPilot.Overlay.Tests;

internal static class TarkovDevQuestProviderTests
{
    internal const int TestCount = 3;

    private const string MapsJson = """
        {
          "data": {
            "maps": {
              "woods-key": { "id": "woods-id", "normalizedName": "woods" },
              "customs-key": { "id": "customs-id", "normalizedName": "customs" }
            }
          }
        }
        """;

    private const string TasksJson = """
        {
          "data": {
            "tasks": {
              "located": {
                "id": "quest-located",
                "name": "quest-located name",
                "normalizedName": "search-mission",
                "objectives": [
                  {
                    "id": "visit",
                    "zones": [
                      { "map": "woods-id", "position": { "x": 10, "y": 1, "z": 20 } },
                      { "map": "customs-id", "position": { "x": 30, "y": 2, "z": 40 } }
                    ]
                  },
                  {
                    "id": "item",
                    "possibleLocations": [
                      { "map": "woods-id", "positions": [
                        { "x": 50, "y": 3, "z": 60 }
                      ] }
                    ]
                  }
                ]
              },
              "without-location": {
                "id": "quest-global",
                "name": "quest-global name",
                "normalizedName": "global-task",
                "objectives": [
                  { "id": "kills", "type": "shoot", "maps": ["woods-id"] }
                ]
              },
              "invalid-location": {
                "id": "quest-invalid",
                "name": "quest-invalid name",
                "normalizedName": "invalid-task",
                "objectives": [
                  { "id": "bad", "zones": [
                    { "map": "woods-id", "position": { "x": 5 } }
                  ] }
                ]
              }
            }
          }
        }
        """;

    private const string FrenchJson = """
        { "data": {
          "quest-located name": "Mission de recherche",
          "quest-global name": "Quête globale",
          "quest-invalid name": "Quête invalide"
        } }
        """;

    private const string EnglishJson = """
        { "data": {
          "quest-located name": "Search Mission",
          "quest-global name": "Global Task",
          "quest-invalid name": "Invalid Task"
        } }
        """;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("quêtes géolocalisées de la carte", KeepsOnlyLocatedTasksForMap, failures);
        Run("traductions bilingues des quêtes", KeepsFrenchAndEnglishAliases, failures);
        Run("carte de quêtes inconnue explicite", RejectsUnknownMap, failures);
    }

    private static void KeepsOnlyLocatedTasksForMap()
    {
        var quests = TarkovDevQuestProvider.Parse(
            TasksJson,
            FrenchJson,
            EnglishJson,
            MapsJson,
            "WOODS");

        Require(quests.Count == 1, $"1 quête géolocalisée attendue, reçu {quests.Count}");
        Require(quests[0].Id == "quest-located", "la mauvaise quête a été gardée");
        Require(quests[0].LocationCount == 2,
            $"2 emplacements attendus, reçu {quests[0].LocationCount}");
    }

    private static void KeepsFrenchAndEnglishAliases()
    {
        var quest = TarkovDevQuestProvider.Parse(
            TasksJson,
            FrenchJson,
            EnglishJson,
            MapsJson,
            "woods").Single();

        Require(quest.Name == "Mission de recherche", "le nom français devrait être affiché");
        Require(quest.Aliases.Contains("Search Mission"), "le nom anglais devrait rester un alias OCR");
        Require(quest.Aliases.Any(alias =>
                string.Equals(alias, "search mission", StringComparison.OrdinalIgnoreCase)),
            "le nom normalisé devrait rester couvert par les alias OCR");
    }

    private static void RejectsUnknownMap()
    {
        try
        {
            _ = TarkovDevQuestProvider.Parse(
                TasksJson,
                FrenchJson,
                EnglishJson,
                MapsJson,
                "moon");
            throw new InvalidOperationException("une carte inconnue devrait échouer");
        }
        catch (KeyNotFoundException)
        {
        }
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
