using System.Net;
using System.Net.Http;
using System.Text;

namespace RaidPilot.Overlay.Tests;

internal static class TarkovDevExtractionProviderTests
{
    internal const int TestCount = 9;

    private const string MapsJson = """
        {
          "data": {
            "maps": {
              "map-id": {
                "normalizedName": "customs",
                "extracts": [
                  {
                    "id": "ordinary-by-name",
                    "name": "ex_customs_pmc_crossroads",
                    "faction": "pmc",
                    "position": { "x": 10.5, "y": -2, "z": 30.25 },
                    "switch": false,
                    "switches": ["global-customs-switch"],
                    "transferItem": null
                  },
                  {
                    "id": "f4cdf3dbe54ef647a2a798266a39f1761f871b96",
                    "name": "renamed_trailer_park",
                    "faction": "pmc",
                    "position": { "x": 11, "y": -2, "z": 31 }
                  },
                  {
                    "id": "conditional",
                    "name": "ex_conditional",
                    "faction": "shared",
                    "position": { "x": -7, "y": 1.5, "z": 99 },
                    "switches": ["power-id"],
                    "transferItem": { "item": "roubles", "count": 20000 }
                  },
                  {
                    "id": "legacy-switch",
                    "name": "Already readable",
                    "faction": "scav",
                    "position": { "x": 1, "y": 2, "z": 3 },
                    "switch": "legacy-id"
                  },
                  {
                    "id": "flare",
                    "name": "ex_customs_pmc_flare",
                    "faction": "pmc",
                    "position": { "x": 4, "y": 5, "z": 6 }
                  },
                  {
                    "id": "powered",
                    "name": "ex_customs_pmc_zb1013",
                    "faction": "pmc",
                    "position": { "x": 7, "y": 8, "z": 9 }
                  },
                  {
                    "id": "missing-position",
                    "name": "ignored",
                    "faction": "pmc"
                  }
                ]
              },
              "other-id": {
                "normalizedName": "woods",
                "extracts": []
              }
            }
          }
        }
        """;

    private const string FrenchTranslationsJson = """
        {
          "data": {
            "ex_customs_pmc_crossroads": "Carrefour",
            "renamed_trailer_park": "Nom futur du parc à remorques",
            "ex_conditional": "Sortie coopérative",
            "ex_customs_pmc_flare": "Voie ferrée (fusée détresse)",
            "ex_customs_pmc_zb1013": "ZB-1013",
            "ignored_number": 42
          }
        }
        """;

    private const string EnglishTranslationsJson = """
        {
          "data": {
            "ex_customs_pmc_crossroads": "Crossroads",
            "renamed_trailer_park": "Future Trailer Park name",
            "ex_conditional": "Cooperative Exit",
            "ex_customs_pmc_flare": "Railroad Passage (signal flare)",
            "ex_customs_pmc_zb1013": "ZB-1013"
          }
        }
        """;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("chargement des extractions traduites", ParsesRequestedMapAndTranslations, failures);
        Run("marquage des conditions fiables", MarksReliableRequirements, failures);
        Run("interrupteur global sans faux positif", KeepsAllowListedExitOrdinaryDespiteMapSwitch, failures);
        Run("sortie inconnue refusée par défaut", MarksUnlistedExitUnknown, failures);
        Run("identité stable autorisée après renommage", AllowsAuditedStableIdAfterRename, failures);
        Run("liste blanche limitée à la carte", KeepsAllowListMapScoped, failures);
        Run("conditions externes prudentes", MarksKnownExternalRequirements, failures);
        Run("carte inconnue explicite", RejectsUnknownMap, failures);
        RunAsync("chargement des deux URL officielles", LoadsOfficialEndpointsAsync, failures);
    }

    private static void ParsesRequestedMapAndTranslations()
    {
        var candidates = TarkovDevExtractionProvider.Parse(
            MapsJson,
            FrenchTranslationsJson,
            "CUSTOMS",
            EnglishTranslationsJson);

        Require(candidates.Count == 6,
            $"6 extractions valides attendues, reçu {candidates.Count}");
        var plain = candidates.Single(candidate => candidate.Id == "ordinary-by-name");
        Require(plain.Name == "Carrefour", "la clé de traduction française n'a pas été résolue");
        Require(plain.Aliases?.Contains("Crossroads") == true,
            "le nom anglais devrait être conservé pour retrouver le marqueur");
        Require(plain.Faction == "pmc", "la faction n'a pas été conservée");
        Near(plain.X, 10.5, "x");
        Near(plain.Y, -2, "y");
        Near(plain.Z, 30.25, "z");

        var fallback = candidates.Single(candidate => candidate.Id == "legacy-switch");
        Require(fallback.Name == "Already readable",
            "le nom brut devrait servir de repli quand aucune traduction n'existe");
    }

    private static void MarksReliableRequirements()
    {
        var candidate = TarkovDevExtractionProvider.Parse(
                MapsJson,
                FrenchTranslationsJson,
                "customs",
                EnglishTranslationsJson)
            .Single(item => item.Id == "conditional");

        var expected = ExtractionRequirement.Transfer
            | ExtractionRequirement.Cooperation;
        Require(candidate.Requirements == expected,
            $"conditions attendues {expected}, reçues {candidate.Requirements}");
    }

    private static void KeepsAllowListedExitOrdinaryDespiteMapSwitch()
    {
        var candidate = TarkovDevExtractionProvider.Parse(
                MapsJson,
                FrenchTranslationsJson,
                "customs",
                EnglishTranslationsJson)
            .Single(item => item.Id == "ordinary-by-name");

        Require(candidate.Requirements == ExtractionRequirement.None,
            "l'interrupteur global de Customs ne doit pas rendre Carrefour conditionnelle");
    }

    private static void MarksUnlistedExitUnknown()
    {
        var candidate = TarkovDevExtractionProvider.Parse(
                MapsJson,
                FrenchTranslationsJson,
                "customs",
                EnglishTranslationsJson)
            .Single(item => item.Id == "legacy-switch");

        Require(candidate.Requirements == ExtractionRequirement.Unknown,
            "une sortie non auditée doit rester inconnue même sans condition fiable dans le flux");
    }

    private static void AllowsAuditedStableIdAfterRename()
    {
        var candidate = TarkovDevExtractionProvider.Parse(
                MapsJson,
                FrenchTranslationsJson,
                "customs",
                EnglishTranslationsJson)
            .Single(item => item.Id == "f4cdf3dbe54ef647a2a798266a39f1761f871b96");

        Require(candidate.Requirements == ExtractionRequirement.None,
            "l'identifiant stable audité de Trailer Park devrait survivre à un renommage du libellé");
    }

    private static void KeepsAllowListMapScoped()
    {
        const string woodsJson = """
            {
              "data": {
                "maps": {
                  "woods-id": {
                    "normalizedName": "woods",
                    "extracts": [
                      {
                        "id": "ordinary-by-name",
                        "name": "ex_customs_pmc_crossroads",
                        "faction": "pmc",
                        "position": { "x": 1, "y": 2, "z": 3 }
                      }
                    ]
                  }
                }
              }
            }
            """;

        var candidate = TarkovDevExtractionProvider.Parse(
                woodsJson,
                FrenchTranslationsJson,
                "woods",
                EnglishTranslationsJson)
            .Single();

        Require(candidate.Requirements == ExtractionRequirement.Unknown,
            "un nom autorisé sur Customs ne doit pas être autorisé sur Woods");
    }

    private static void MarksKnownExternalRequirements()
    {
        var candidates = TarkovDevExtractionProvider.Parse(
            MapsJson,
            FrenchTranslationsJson,
            "customs",
            EnglishTranslationsJson);

        Require((candidates.Single(item => item.Id == "flare").Requirements
                & ExtractionRequirement.Unknown) != 0,
            "la sortie à fusée devrait être conditionnelle");
        Require((candidates.Single(item => item.Id == "powered").Requirements
                & ExtractionRequirement.Unknown) != 0,
            "ZB-1013 devrait être conditionnelle");
    }

    private static void RejectsUnknownMap()
    {
        try
        {
            _ = TarkovDevExtractionProvider.Parse(
                MapsJson,
                FrenchTranslationsJson,
                "labs");
            throw new InvalidOperationException("aucune exception pour une carte absente");
        }
        catch (KeyNotFoundException)
        {
            // Attendu : une absence de carte ne doit pas ressembler à une carte
            // valide sans extraction.
        }
    }

    private static async Task LoadsOfficialEndpointsAsync()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var body = request.RequestUri == TarkovDevExtractionProvider.MapsUri
                ? MapsJson
                : request.RequestUri == TarkovDevExtractionProvider.FrenchTranslationsUri
                    ? FrenchTranslationsJson
                    : request.RequestUri == TarkovDevExtractionProvider.EnglishTranslationsUri
                        ? EnglishTranslationsJson
                    : throw new InvalidOperationException($"URL inattendue: {request.RequestUri}");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler);
        var provider = new TarkovDevExtractionProvider(client);

        var result = await provider.LoadAsync("customs");

        Require(result.Count == 6, "le chargement HTTP n'a pas retourné les extractions parsées");
        Require(handler.RequestedUris.Count == 3, "les trois ressources n'ont pas été demandées");
        Require(handler.RequestedUris.Contains(TarkovDevExtractionProvider.MapsUri),
            "l'URL maps n'a pas été utilisée");
        Require(handler.RequestedUris.Contains(TarkovDevExtractionProvider.FrenchTranslationsUri),
            "l'URL maps_fr n'a pas été utilisée");
        Require(handler.RequestedUris.Contains(TarkovDevExtractionProvider.EnglishTranslationsUri),
            "l'URL maps_en n'a pas été utilisée");
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

    private static void RunAsync(string name, Func<Task> test, ICollection<string> failures)
    {
        Run(name, () => test().GetAwaiter().GetResult(), failures);
    }

    private static void Near(double actual, double expected, string label, double tolerance = 0.0001)
    {
        Require(Math.Abs(actual - expected) <= tolerance,
            $"{label} attendu {expected:F4}, reçu {actual:F4}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        internal List<Uri> RequestedUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedUris.Add(request.RequestUri
                ?? throw new InvalidOperationException("La requête ne contient aucune URL."));
            var response = responseFactory(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
