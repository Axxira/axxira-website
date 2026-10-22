namespace RaidPilot.Overlay.Tests;

internal static class ExtractionAdvisorTests
{
    internal const int TestCount = 17;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("extraction confirmée la plus proche", ChoosesNearestConfirmedExtraction, failures);
        Run("aucune suggestion non confirmée par défaut", RefusesUnknownAvailabilityByDefault, failures);
        Run("suggestion de secours clairement marquée", MarksUnconfirmedFallback, failures);
        Run("liste confirmée vide autoritaire", RespectsEmptyConfirmedSet, failures);
        Run("cible utilisateur stable", KeepsPreferredTarget, failures);
        Run("sorties conditionnelles sur accord", RequiresExplicitConditionalOptIn, failures);
        Run("Reserve garde un guidage EXFIL conditionnel confirmé", FallsBackToConfirmedConditionalReserveExit, failures);
        Run("résolution sûre des noms d'extraction", ResolvesOnlyUniqueNormalizedNames, failures);
        Run("lecture des lignes OCR EFT réelles", ResolvesPrefixedOcrLinesAndAliases, failures);
        Run("préfixes EXFIL OCR tolérants et dédupliqués", ReadsConfusedAndDuplicateExfilSlots, failures);
        Run("panneau OCR complet rang par rang", RequiresOneUniqueMatchPerExfilSlot, failures);
        Run("panneau Woods partiel garde les sorties sûres", KeepsSafeOrdinaryWoodsExitsFromPartialPanel, failures);
        Run("ligne OCR d’extraction repliée", JoinsWrappedExtractionName, failures);
        Run("confusion OCR ZB-1011 sans chiffre initial", ResolvesZb1011WithoutLiteralDigits, failures);
        Run("rejet des alias OCR ambigus", RejectsAmbiguousOcrAliases, failures);
        Run("rejet des corrections OCR à égalité", RejectsTiedFuzzyCorrections, failures);
        Run("bascule automatique vers la plus proche", SwitchesNearestTargetAfterMidpoint, failures);
    }

    private static void ChoosesNearestConfirmedExtraction()
    {
        var player = PlayerAt(0, 4, 0);
        var candidates = new[]
        {
            Extract("far", "Sortie lointaine", "pmc", 0, 0, 300),
            Extract("near", "Sortie proche", "pmc", 30, 9, 40),
            Extract("not-this-raid", "Encore plus proche", "pmc", 1, 4, 1),
        };
        var confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FAR", "near" };

        Require(ExtractionAdvisor.TryChoose(
                player,
                RaidSide.Pmc,
                candidates,
                confirmed,
                out var recommendation),
            "aucune extraction n'a été choisie");
        Require(recommendation is not null, "la recommandation est nulle");
        var selected = recommendation!;
        Require(selected.Target.Id == "near", "la sortie confirmée la plus proche n'a pas été choisie");
        Near(selected.DirectDistanceMeters, 50, "distance X/Z");
        Near(selected.VerticalDeltaMeters, 5, "écart vertical");
        Require(selected.Reason == ExtractionSelectionReason.ConfirmedNearest,
            "raison de sélection incorrecte");
        Require(!selected.NeedsAvailabilityConfirmation,
            "une sortie confirmée ne doit pas redemander confirmation");
    }

    private static void RefusesUnknownAvailabilityByDefault()
    {
        Require(!ExtractionAdvisor.TryChoose(
                PlayerAt(0, 0, 0),
                RaidSide.Pmc,
                new[] { Extract("possible", "Possible", "pmc", 10, 0, 0) },
                confirmedExtractionIds: null,
                out var recommendation),
            "une sortie seulement possible a été présentée comme confirmée");
        Require(recommendation is null, "un résultat résiduel a été retourné");
    }

    private static void MarksUnconfirmedFallback()
    {
        Require(ExtractionAdvisor.TryChoose(
                PlayerAt(0, 0, 0),
                RaidSide.Pmc,
                new[] { Extract("possible", "Possible", null, 10, 0, 0) },
                confirmedExtractionIds: null,
                out var recommendation,
                allowUnconfirmedFallback: true),
            "la suggestion de secours n'a pas été produite");
        Require(recommendation is not null, "la suggestion est nulle");
        var selected = recommendation!;
        Require(selected.NeedsAvailabilityConfirmation,
            "la suggestion non confirmée n'est pas signalée");
        Require(selected.Reason == ExtractionSelectionReason.UnconfirmedFallback,
            "la raison de secours est incorrecte");
    }

    private static void RespectsEmptyConfirmedSet()
    {
        Require(!ExtractionAdvisor.TryChoose(
                PlayerAt(0, 0, 0),
                RaidSide.Pmc,
                new[] { Extract("possible", "Possible", "pmc", 10, 0, 0) },
                new HashSet<string>(),
                out _,
                allowUnconfirmedFallback: true),
            "une liste confirmée vide ne doit pas être remplacée par une supposition");
    }

    private static void KeepsPreferredTarget()
    {
        var candidates = new[]
        {
            Extract("near", "Proche", "pmc", 10, 0, 0),
            Extract("locked", "Cible choisie", "pmc", 100, 0, 0),
        };
        var confirmed = new HashSet<string> { "near", "locked" };

        Require(ExtractionAdvisor.TryChoose(
                PlayerAt(0, 0, 0),
                RaidSide.Pmc,
                candidates,
                confirmed,
                out var recommendation,
                preferredId: "LOCKED"),
            "la cible choisie n'a pas été conservée");
        Require(recommendation?.Target.Id == "locked", "la cible a changé vers la plus proche");
        Require(recommendation?.Reason == ExtractionSelectionReason.UserPreferred,
            "le verrouillage utilisateur n'est pas indiqué");
    }

    private static void RequiresExplicitConditionalOptIn()
    {
        var ordinaryShared = Extract("shared", "Sortie partagée ordinaire", "shared", 5, 0, 0);
        Require(ExtractionAdvisor.EffectiveRequirements(ordinaryShared) == ExtractionRequirement.None,
            "une faction partagée ne prouve pas que la sortie exige une coopération");

        var shared = Extract(
            "coop",
            "Sortie coop",
            "shared",
            10,
            0,
            0,
            ExtractionRequirement.Cooperation);
        var confirmed = new HashSet<string> { "coop" };

        Require(!ExtractionAdvisor.TryChoose(
                PlayerAt(0, 0, 0),
                RaidSide.Pmc,
                new[] { shared },
                confirmed,
                out _),
            "la sortie coop a été choisie sans accord explicite");
        Require(ExtractionAdvisor.TryChoose(
                PlayerAt(0, 0, 0),
                RaidSide.Pmc,
                new[] { shared },
                confirmed,
                out var recommendation,
                allowConditional: true),
            "la sortie coop explicitement autorisée a été refusée");
        Require(recommendation?.Target.Id == "coop", "mauvaise sortie conditionnelle");
    }

    private static void FallsBackToConfirmedConditionalReserveExit()
    {
        var candidates = new[]
        {
            Extract("d2", "D-2", "pmc", 120, 0, 0, ExtractionRequirement.Unknown),
            Extract("bunker", "Porte hermétique du bunker", "pmc", 80, 0, 0, ExtractionRequirement.Switch),
            Extract("sewer", "Bouche d'égout", "pmc", 20, 0, 0, ExtractionRequirement.Unknown),
            Extract("transit", "Transit vers les douanes", "pmc", 1, 0, 0),
            Extract("unknown-rank", "Sortie vers les bois", "pmc", 2, 0, 0),
            Extract("unconfirmed", "Sortie non offerte", "pmc", 3, 0, 0),
        };
        var panel = ExtractionAdvisor.ResolveOcrExtractionPanel(
            candidates,
            [
                "EXFILØI D-2",
                "EXFILØ2 Porte hermétique du bunker",
                "EXFILØ3 Bouche d'égout",
                "TRANSITØI Transit vers les douanes",
                "EXFIL?? Sortie vers les bois",
            ]);

        Require(panel.IsComplete && panel.ConfirmedIds.SetEquals(new[] { "d2", "bunker", "sewer" }),
            $"seules les trois lignes EXFIL numérotées devraient être confirmées "
            + $"(rangs={panel.SlotCount}, IDs={string.Join(", ", panel.ConfirmedIds)})");
        Require(ExtractionAdvisor.TryChooseConfirmedWithConditionalFallback(
                PlayerAt(0, 0, 0),
                RaidSide.Pmc,
                candidates,
                panel.ConfirmedIds,
                out var fallback),
            "aucun guidage n'a été produit quand Reserve ne proposait que des EXFIL conditionnelles");
        Require(fallback?.Target.Id == "sewer",
            "la sortie conditionnelle confirmée la plus proche n'a pas été choisie");

        var withOrdinary = candidates.Append(
            Extract("ordinary", "Sortie sans condition", "pmc", 500, 0, 0)).ToArray();
        var confirmedWithOrdinary = new HashSet<string>(panel.ConfirmedIds, StringComparer.OrdinalIgnoreCase)
        {
            "ordinary",
        };
        Require(ExtractionAdvisor.TryChooseConfirmedWithConditionalFallback(
                PlayerAt(0, 0, 0),
                RaidSide.Pmc,
                withOrdinary,
                confirmedWithOrdinary,
                out var preferred),
            "la sortie sans condition confirmée n'a pas été choisie");
        Require(preferred?.Target.Id == "ordinary",
            "une sortie conditionnelle plus proche a remplacé la sortie sans condition");
    }

    private static void ResolvesOnlyUniqueNormalizedNames()
    {
        var candidates = new[]
        {
            Extract("road", "Route vers l’extraction", "pmc", 0, 0, 0),
            Extract("gate-a", "Porte", "pmc", 0, 0, 0),
            Extract("gate-b", "Porte", "scav", 0, 0, 0),
        };

        var resolved = ExtractionAdvisor.ResolveConfirmedIdsByDisplayedName(
            candidates,
            new[] { "  ROUTE vers l'extraction!! ", "Porte", "Texte OCR inconnu" });

        Require(resolved.SetEquals(new[] { "road" }),
            "la normalisation exacte ou le rejet d'un nom ambigu a échoué");
    }

    private static void ResolvesPrefixedOcrLinesAndAliases()
    {
        var candidates = new[]
        {
            new ExtractionCandidate(
                "crossroads", "Carrefour", "pmc", 0, 0, 0,
                Aliases: new[] { "Carrefour", "Crossroads" }),
            new ExtractionCandidate(
                "trailer", "Parc à remorques", "pmc", 0, 0, 0,
                Aliases: new[] { "Parc à remorques", "Trailer Park" }),
            new ExtractionCandidate(
                "zb1013", "ZB-1013", "pmc", 0, 0, 0,
                Aliases: new[] { "ZB-1013" }),
        };

        var resolved = ExtractionAdvisor.ResolveConfirmedIdsFromOcrLines(
            candidates,
            new[]
            {
                "EXFILØ3 Carrefour",
                "EXFILØ4 Parc å remorques",
                "EXFILØ2 ZB-lØ13",
            });

        Require(resolved.SetEquals(new[] { "crossroads", "trailer", "zb1013" }),
            $"lignes OCR mal résolues : {string.Join(", ", resolved)}");
    }

    private static void RejectsAmbiguousOcrAliases()
    {
        var candidates = new[]
        {
            new ExtractionCandidate(
                "gate-a", "Porte A", "pmc", 0, 0, 0,
                Aliases: new[] { "Gate" }),
            new ExtractionCandidate(
                "gate-b", "Porte B", "pmc", 0, 0, 0,
                Aliases: new[] { "Gate" }),
        };

        var resolved = ExtractionAdvisor.ResolveConfirmedIdsFromOcrLines(
            candidates,
            new[] { "EXFIL01 Gate" });

        Require(resolved.Count == 0, "un alias partagé ne doit confirmer aucune sortie");
    }

    private static void RejectsTiedFuzzyCorrections()
    {
        var candidates = new[]
        {
            Extract("old-road", "Old Road", "pmc", 0, 0, 0),
            Extract("old-roads", "Old Roads", "pmc", 0, 0, 0),
        };

        var resolved = ExtractionAdvisor.ResolveConfirmedIdsFromOcrLines(
            candidates,
            ["EXFIL01 Old Roadz"]);

        Require(resolved.Count == 0,
            "une correction OCR à égale distance a choisi arbitrairement une sortie");
    }

    private static void ReadsConfusedAndDuplicateExfilSlots()
    {
        var lines = ExtractionAdvisor.SelectUniqueOcrExfilLines(
        [
            "EXF1L01 Carrefour",
            "EXFILØI Carref",
            "EXFILØ2 Parc à remorques",
            "HUD Carrefour",
        ]);

        Require(lines.Count == 2, $"rangs EXFIL incorrects : {lines.Count}");
        Require(lines.Any(line => line.Contains("Carrefour", StringComparison.Ordinal)),
            "la meilleure variante du rang EXFIL01 n'a pas été gardée");
    }

    private static void RequiresOneUniqueMatchPerExfilSlot()
    {
        var candidates = new[]
        {
            Extract("crossroads", "Carrefour", "pmc", 0, 0, 0),
            Extract("trailer", "Parc à remorques", "pmc", 0, 0, 0),
        };

        var incomplete = ExtractionAdvisor.ResolveOcrExtractionPanel(
            candidates,
            ["EXFIL01 Inconnu", "EXFIL02 Parc à remorques", "HUD Carrefour"]);
        Require(incomplete.SlotCount == 2 && incomplete.ConfirmedIds.Count == 1,
            "une ligne HUD a artificiellement complété le panneau");
        Require(!incomplete.IsComplete, "un panneau partiel a été déclaré complet");

        var complete = ExtractionAdvisor.ResolveOcrExtractionPanel(
            candidates,
            ["EXF1L01 Carrefour", "EXFILØ2 Parc à remorques"]);
        Require(complete.IsComplete
            && complete.ConfirmedIds.SetEquals(new[] { "crossroads", "trailer" }),
            "le panneau complet n'a pas été confirmé rang par rang");
    }

    private static void ResolvesZb1011WithoutLiteralDigits()
    {
        var panel = ExtractionAdvisor.ResolveOcrExtractionPanel(
            [Extract("zb1011", "ZB-1011", "pmc", 0, 0, 0),
             Extract("crossroads", "Carrefour", "pmc", 0, 0, 0)],
            ["EXFILØI ZB-IØII", "EXFILØ2 Carrefour"]);

        Require(panel.IsComplete && panel.ConfirmedIds.Contains("zb1011"),
            "ZB-IØII n'a pas été corrigé vers ZB-1011");
    }

    private static void KeepsSafeOrdinaryWoodsExitsFromPartialPanel()
    {
        var candidates = new[]
        {
            Extract("coop", "Pont de l’amitié (Co-Op)", "shared", 0, 0, 0,
                ExtractionRequirement.Cooperation),
            Extract("ruaf", "Porte RUAF", "pmc", 0, 0, 0),
            Extract("zb016", "ZB-016", "pmc", 0, 0, 0,
                ExtractionRequirement.Unknown),
            Extract("un", "Barrage ONU", "pmc", 0, 0, 0),
            Extract("vehicle", "Véhicule du pont", "pmc", 0, 0, 0,
                ExtractionRequirement.Transfer),
            Extract("north", "Barrage ONU nord", "pmc", 0, 0, 0),
            Extract("flare", "Ligne haute tension (fusée détresse)", "pmc", 0, 0, 0,
                ExtractionRequirement.Unknown),
        };
        var panel = ExtractionAdvisor.ResolveOcrExtractionPanel(
            candidates,
            [
                "EXFILØI Pont de Itamitié (CQ-Op)?",
                "EXFILØ2 Porte",
                "EXFILØ3 ZB-Ø16",
                "EXFILØ4 Barrage ONU",
                "EXFILØ5 Véhicule du pont",
                "EXFILØ6 Barrage ONU nord",
                "EXFILØ7 tension (fusée",
            ]);

        var ordinary = candidates
            .Where(candidate => panel.ConfirmedIds.Contains(candidate.Id))
            .Where(candidate => ExtractionAdvisor.EffectiveRequirements(candidate)
                == ExtractionRequirement.None)
            .Select(candidate => candidate.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Require(panel.SlotCount == 7 && !panel.IsComplete,
            "le panneau OCR réel de Woods devrait rester partiel");
        Require(panel.ConfirmedIds.SetEquals(new[] { "zb016", "un", "vehicle", "north" }),
            $"IDs Woods inattendus : {string.Join(", ", panel.ConfirmedIds)}");
        Require(ordinary.SetEquals(new[] { "un", "north" }),
            "les deux sorties ordinaires reconnues n’ont pas été conservées");
    }

    private static void JoinsWrappedExtractionName()
    {
        var panel = ExtractionAdvisor.ResolveOcrExtractionPanel(
            [
                Extract("flare", "Ligne haute tension (fusée détresse)", "pmc", 0, 0, 0,
                    ExtractionRequirement.Unknown),
                Extract("un", "Barrage ONU", "pmc", 0, 0, 0),
            ],
            [
                "EXFIL01 Ligne haute tension (fusée",
                "détresse)",
                "EXFIL02 Barrage ONU",
            ]);

        Require(panel.IsComplete && panel.ConfirmedIds.SetEquals(new[] { "flare", "un" }),
            "la suite de nom OCR entre parenthèses n’a pas été rattachée à son rang EXFIL");
    }

    private static void SwitchesNearestTargetAfterMidpoint()
    {
        var candidates = new[]
        {
            Extract("west", "Ouest", "pmc", -100, 0, 0),
            Extract("east", "Est", "pmc", 100, 0, 0),
        };
        var confirmed = new HashSet<string> { "west", "east" };

        Require(ExtractionAdvisor.TryChoose(
                PlayerAt(-60, 0, 0),
                RaidSide.Pmc,
                candidates,
                confirmed,
                out var west),
            "aucune sortie choisie à l'ouest");
        Require(ExtractionAdvisor.TryChoose(
                PlayerAt(60, 0, 0),
                RaidSide.Pmc,
                candidates,
                confirmed,
                out var east),
            "aucune sortie choisie à l'est");

        Require(west?.Target.Id == "west" && east?.Target.Id == "east",
            "la cible la plus proche n'a pas changé après le milieu");
    }

    private static ScreenshotObservation PlayerAt(double x, double y, double z)
    {
        return new ScreenshotObservation("test.png", x, y, z, 0, 0, 0, 1, 0);
    }

    private static ExtractionCandidate Extract(
        string id,
        string name,
        string? faction,
        double x,
        double y,
        double z,
        ExtractionRequirement requirements = ExtractionRequirement.None)
    {
        return new ExtractionCandidate(id, name, faction, x, y, z, requirements);
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

    private static void Near(double actual, double expected, string label, double tolerance = 0.001)
    {
        Require(Math.Abs(actual - expected) <= tolerance,
            $"{label} attendu {expected:F3}, reçu {actual:F3}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
