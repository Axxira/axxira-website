namespace RaidPilot.Overlay.Tests;

internal static class LearnedRouteGraphTests
{
    internal const int TestCount = 20;

    private static readonly DateTimeOffset Epoch = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    internal static void RunAll(ICollection<string> failures)
    {
        Run("graphe appris conserve le détour réellement parcouru", KeepsTraversedCorner, failures);
        Run("arêtes apprises dirigées sans retour inventé", RequiresObservedReverseTraversal, failures);
        Run("étages superposés séparés", SeparatesOverlappingFloors, failures);
        Run("escalier observé relie les hauteurs", ConnectsObservedVerticalTransition, failures);
        Run("extraction atteignable préférée à la plus proche isolée", ChoosesNearestReachableTarget, failures);
        Run("trou temporel ne crée aucune arête", DoesNotBridgeSampleGap, failures);
        Run("rupture explicite interdit une arête de recalage", BreaksContinuityBeforeCorrection, failures);
        Run("pose live trop incertaine casse la continuité", RejectsUncertainLivePose, failures);
        Run("bornes de nœuds et d'arêtes respectées", KeepsGraphBounded, failures);
        Run("sessions proches ne créent aucun passage implicite", DoesNotJoinNearbySessions, failures);
        Run("cartes apprises isolées", KeepsMapsIsolated, failures);
        Run("confiance distingue ancres exactes et poses live", ReportsRouteConfidence, failures);
        Run("incertitude courante borne la confiance du plan", CurrentPoseUncertaintyBoundsPlanConfidence, failures);
        Run("A étoile choisit le chemin appris le plus court", AStarChoosesShortestLearnedPath, failures);
        Run("arrivée détectée sans graphe", DetectsArrivalWithoutGraph, failures);
        Run("repli direct peut être interdit", CanDisableDirectFallback, failures);
        Run("repli signale un étage différent", ReportsDifferentHeightFallback, failures);
        Run("poses invalides et anciennes rejetées", RejectsInvalidAndStalePoses, failures);
        Run("persistance ronde conserve routes et sens", PersistsAndRestoresGraph, failures);
        Run("persistance altérée ou inconnue rejetée", RejectsUnsafePersistence, failures);
    }

    private static void KeepsTraversedCorner()
    {
        var graph = CreateGraph();
        graph.BeginSession("customs");
        ObserveExact(graph, 0, 0, 0, 0);
        ObserveExact(graph, 0, 0, 5, 1);
        ObserveExact(graph, 5, 0, 5, 2);
        ObserveExact(graph, 10, 0, 5, 3);

        Require(graph.TryPlanRoute(
            Pose(0, 0, 0, 4),
            new[] { Target("extract", 10, 0, 5) },
            out var plan), "aucune route");
        Require(plan is { Kind: LearnedRouteKind.LearnedGraph }, "la route apprise n'est pas utilisée");
        Require(plan!.UsesOnlyTraversedEdges, "la route doit garantir ses arêtes parcourues");
        Require(plan.Waypoints.Any(point => Near(point.X, 0) && Near(point.Z, 5)),
            "le virage parcouru a été supprimé");
        Near(plan.GraphDistanceMeters, 15, "distance du détour");
        Require(plan.GraphDistanceMeters > plan.DirectDistanceMeters,
            "le mur implicite a été traversé en diagonale");
    }

    private static void RequiresObservedReverseTraversal()
    {
        var graph = CreateGraph();
        graph.BeginSession("customs");
        ObserveExact(graph, 0, 0, 0, 0);
        ObserveExact(graph, 5, 0, 0, 1);
        ObserveExact(graph, 10, 0, 0, 2);

        Require(graph.TryPlanRoute(
            Pose(10, 0, 0, 3),
            new[] { Target("west", 0, 0, 0) },
            out var fallback), "aucun résultat de secours");
        Require(fallback is
        {
            Kind: LearnedRouteKind.DirectFallback,
            FallbackReason: LearnedRouteFallbackReason.LearnedComponentsDisconnected,
            UsesOnlyTraversedEdges: false,
        }, "le retour non parcouru a été inventé");

        graph.BeginSession("customs");
        ObserveExact(graph, 10, 0, 0, 10);
        ObserveExact(graph, 5, 0, 0, 11);
        ObserveExact(graph, 0, 0, 0, 12);
        Require(graph.TryPlanRoute(
            Pose(10, 0, 0, 13),
            new[] { Target("west", 0, 0, 0) },
            out var learned), "aucune route retour");
        Require(learned?.Kind == LearnedRouteKind.LearnedGraph, "le retour observé n'a pas été appris");
    }

    private static void SeparatesOverlappingFloors()
    {
        var graph = CreateGraph();
        graph.BeginSession("interchange");
        ObserveExact(graph, 0, 0, 0, 0, "interchange");
        graph.BeginSession("interchange");
        ObserveExact(graph, 0, 4, 0, 10, "interchange");

        Require(graph.GetNodeCount("interchange") == 2, "les deux étages ont fusionné");
        Require(graph.GetEdgeCount("interchange") == 0, "un lien vertical implicite a été créé");
        Require(graph.TryPlanRoute(
            Pose(0, 0, 0, 11, map: "interchange"),
            new[] { Target("upper", 0, 4, 0) },
            out var plan), "aucun repli");
        Require(plan?.Kind == LearnedRouteKind.DirectFallback, "un changement d'étage a été inventé");
    }

    private static void ConnectsObservedVerticalTransition()
    {
        var graph = CreateGraph();
        graph.BeginSession("factory");
        ObserveExact(graph, 0, 0, 0, 0, "factory");
        ObserveExact(graph, 0, 1, 2, 1, "factory");
        ObserveExact(graph, 0, 2, 4, 2, "factory");
        ObserveExact(graph, 0, 3, 6, 3, "factory");

        Require(graph.TryPlanRoute(
            Pose(0, 0, 0, 4, map: "factory"),
            new[] { Target("upper", 0, 3, 6) },
            out var plan), "aucune route verticale");
        Require(plan?.Kind == LearnedRouteKind.LearnedGraph, "l'escalier observé n'est pas utilisé");
        Require(plan!.Waypoints.Select(point => point.HeightBand).Distinct().Count() >= 2,
            "les bandes de hauteur ne sont pas conservées");
    }

    private static void ChoosesNearestReachableTarget()
    {
        var graph = CreateGraph();
        graph.BeginSession("customs");
        ObserveExact(graph, 0, 0, 0, 0);
        ObserveExact(graph, 5, 0, 0, 1);
        ObserveExact(graph, 10, 0, 0, 2);
        graph.BeginSession("customs");
        ObserveExact(graph, 2, 0, 2, 10);

        Require(graph.TryPlanRoute(
            Pose(0, 0, 0, 11),
            new[]
            {
                Target("near-island", 2, 0, 2),
                Target("far-reachable", 10, 0, 0),
            },
            out var plan), "aucune route");
        Require(plan?.Target.Id == "far-reachable", "l'extraction isolée a été choisie");
        Require(plan!.Kind == LearnedRouteKind.LearnedGraph, "le chemin accessible n'est pas utilisé");
    }

    private static void DoesNotBridgeSampleGap()
    {
        var graph = CreateGraph();
        graph.BeginSession("customs");
        ObserveExact(graph, 0, 0, 0, 0);
        ObserveExact(graph, 5, 0, 0, 20);
        Require(graph.GetEdgeCount("customs") == 0, "un trou temporel a produit une arête");

        Require(graph.TryPlanRoute(
            Pose(0, 0, 0, 21),
            new[] { Target("end", 5, 0, 0) },
            out var plan), "aucun repli");
        Require(plan?.FallbackReason == LearnedRouteFallbackReason.LearnedComponentsDisconnected,
            "le graphe discontinu n'est pas signalé");
    }

    private static void BreaksContinuityBeforeCorrection()
    {
        foreach (var correctionMeters in new[] { 1.0, 4.0, 8.0 })
        {
            var graph = CreateGraph(TestOptions() with
            {
                TargetSnapHorizontalRadiusMeters = 0.25,
                ArrivalHorizontalRadiusMeters = 0.10,
            });
            graph.BeginSession("customs");
            ObserveExact(graph, 0, 0, 0, 0);

            graph.BreakContinuity();
            graph.BreakContinuity(); // La rupture est volontairement idempotente.
            ObserveExact(graph, correctionMeters, 0, 0, 1);

            Require(graph.GetNodeCount("customs") == 2,
                $"le recalage de {correctionMeters:F0} m n'a pas amorcé un nouveau segment");
            Require(graph.GetEdgeCount("customs") == 0,
                $"le recalage de {correctionMeters:F0} m a créé une arête fictive");
            Require(graph.TryPlanRoute(
                Pose(0, 0, 0, 2),
                new[] { Target("corrected", correctionMeters, 0, 0) },
                out var fallback), "aucun repli aprè rupture");
            Require(fallback?.Kind == LearnedRouteKind.DirectFallback,
                $"le recalage de {correctionMeters:F0} m est présenté comme un trajet parcouru");
        }

        // L'appel reste sûr avant ou aprè une session.
        var idle = CreateGraph();
        idle.BreakContinuity();
        idle.BeginSession("customs");
        idle.EndSession();
        idle.BreakContinuity();
    }

    private static void RejectsUncertainLivePose()
    {
        var graph = CreateGraph();
        graph.BeginSession("customs");
        ObserveExact(graph, 0, 0, 0, 0);
        var rejected = graph.Observe(Pose(
            2, 0, 0, 1,
            source: LearnedPoseSource.LiveEstimate,
            uncertainty: 50));
        Require(rejected.Status == LearnedRouteObservationStatus.RejectedUncertain,
            "la pose trop incertaine est acceptée");
        ObserveExact(graph, 4, 0, 0, 2);
        Require(graph.GetEdgeCount("customs") == 0, "la continuité a survécu à une pose non fiable");
    }

    private static void KeepsGraphBounded()
    {
        var graph = CreateGraph(new LearnedRouteGraphOptions
        {
            MaxMaps = 2,
            MaxNodesPerMap = 3,
            MaxEdgesPerMap = 2,
            NodeSpacingMeters = 0.5,
            MergeHorizontalRadiusMeters = 0.2,
            MergeVerticalToleranceMeters = 0.5,
            HeightBandMeters = 2.5,
            MaximumSegmentHorizontalMeters = 10,
            MaximumSegmentVerticalMeters = 2,
            MaximumSampleGap = TimeSpan.FromSeconds(5),
            MaximumLiveUncertaintyMeters = 10,
            StartSnapHorizontalRadiusMeters = 2,
            TargetSnapHorizontalRadiusMeters = 1,
            RouteVerticalToleranceMeters = 1,
            ArrivalHorizontalRadiusMeters = 0.2,
        });
        graph.BeginSession("customs");
        for (var index = 0; index < 8; index++)
        {
            ObserveExact(graph, index * 2, 0, 0, index);
        }

        Require(graph.GetNodeCount("customs") == 3, "la limite de nœuds n'est pas respectée");
        Require(graph.GetEdgeCount("customs") <= 2, "la limite d'arêtes n'est pas respectée");
    }

    private static void DoesNotJoinNearbySessions()
    {
        var graph = CreateGraph();
        graph.BeginSession("customs");
        ObserveExact(graph, 0, 0, 0, 0);
        ObserveExact(graph, 4, 0, 0, 1);
        graph.BeginSession("customs");
        ObserveExact(graph, 6, 0, 0, 10);
        ObserveExact(graph, 10, 0, 0, 11);

        Require(graph.GetEdgeCount("customs") == 2, "une arête inter-session a été ajoutée");
        Require(graph.TryPlanRoute(
            Pose(0, 0, 0, 12),
            new[] { Target("far", 10, 0, 0) },
            out var plan), "aucun repli");
        Require(plan?.FallbackReason == LearnedRouteFallbackReason.LearnedComponentsDisconnected,
            "la proximité a créé un passage fictif");
    }

    private static void KeepsMapsIsolated()
    {
        var graph = CreateGraph();
        graph.BeginSession("customs");
        ObserveExact(graph, 0, 0, 0, 0);
        ObserveExact(graph, 5, 0, 0, 1);
        graph.BeginSession("woods");
        ObserveExact(graph, 0, 0, 0, 10, "woods");

        Require(graph.GetNodeCount("customs") == 2, "Customs perd ses nœuds");
        Require(graph.GetNodeCount("woods") == 1, "Woods incorrect");
        Require(graph.TryPlanRoute(
            Pose(0, 0, 0, 11, map: "woods"),
            new[] { Target("east", 5, 0, 0) },
            out var plan), "aucun repli");
        Require(plan?.Kind == LearnedRouteKind.DirectFallback, "une route d'une autre carte a été utilisée");
    }

    private static void ReportsRouteConfidence()
    {
        var exact = CreateGraph();
        exact.BeginSession("customs");
        ObserveExact(exact, 0, 0, 0, 0);
        ObserveExact(exact, 5, 0, 0, 1);
        Require(exact.TryPlanRoute(
            Pose(0, 0, 0, 2),
            new[] { Target("end", 5, 0, 0) },
            out var exactPlan), "route exacte absente");
        Require(exactPlan?.Confidence == LearnedRouteConfidence.High, "la route exacte n'est pas fiable");

        var live = CreateGraph();
        live.BeginSession("customs");
        live.Observe(Pose(0, 0, 0, 0, source: LearnedPoseSource.LiveEstimate, uncertainty: 9));
        live.Observe(Pose(5, 0, 0, 1, source: LearnedPoseSource.LiveEstimate, uncertainty: 9));
        Require(live.TryPlanRoute(
            Pose(0, 0, 0, 2),
            new[] { Target("end", 5, 0, 0) },
            out var livePlan), "route live absente");
        Require(livePlan?.Confidence == LearnedRouteConfidence.Low, "l'incertitude live n'est pas propagée");
        Require(livePlan!.ConfidenceScore < exactPlan!.ConfidenceScore, "les scores de confiance sont inversés");
    }

    private static void CurrentPoseUncertaintyBoundsPlanConfidence()
    {
        var graph = CreateGraph();
        graph.BeginSession("customs");
        ObserveExact(graph, 0, 0, 0, 0);
        ObserveExact(graph, 5, 0, 0, 1);
        ObserveExact(graph, 10, 0, 0, 2);

        Require(graph.TryPlanRoute(
            Pose(
                0,
                0,
                0,
                3,
                source: LearnedPoseSource.LiveEstimate,
                uncertainty: 9),
            new[] { Target("east", 10, 0, 0) },
            out var plan), "route avec pose courante incertaine absente");
        Require(plan?.Kind == LearnedRouteKind.LearnedGraph,
            "la topologie apprise devrait rester disponible");
        Require(plan!.Confidence == LearnedRouteConfidence.Low,
            "la pose courante incertaine ne borne pas la confiance du plan");
        Require(plan.ConfidenceScore <= 0.250001,
            $"confiance attendue <= 0,25, reçu {plan.ConfidenceScore:F3}");
    }

    private static void AStarChoosesShortestLearnedPath()
    {
        var graph = CreateGraph();
        graph.BeginSession("customs");
        ObserveExact(graph, 0, 0, 0, 0);
        ObserveExact(graph, 0, 0, 5, 1);
        ObserveExact(graph, 5, 0, 5, 2);
        ObserveExact(graph, 10, 0, 0, 3);
        graph.BeginSession("customs");
        ObserveExact(graph, 0, 0, 0, 10);
        ObserveExact(graph, 5, 0, 0, 11);
        ObserveExact(graph, 10, 0, 0, 12);

        Require(graph.TryPlanRoute(
            Pose(0, 0, 0, 13),
            new[] { Target("east", 10, 0, 0) },
            out var plan), "aucune route");
        Near(plan!.GraphDistanceMeters, 10, "plus court chemin A étoile");
        Require(plan.Waypoints.Any(point => Near(point.X, 5) && Near(point.Z, 0)),
            "le raccourci réellement parcouru n'est pas utilisé");
    }

    private static void DetectsArrivalWithoutGraph()
    {
        var graph = CreateGraph();
        Require(graph.TryPlanRoute(
            Pose(1, 0, 1, 0),
            new[] { Target("exit", 1.1, 0, 1.1) },
            out var plan), "arrivée non détectée");
        Require(plan is
        {
            Kind: LearnedRouteKind.AlreadyAtTarget,
            FallbackReason: LearnedRouteFallbackReason.None,
            UsesOnlyTraversedEdges: true,
        }, "état d'arrivée incorrect");
    }

    private static void CanDisableDirectFallback()
    {
        var graph = CreateGraph();
        Require(!graph.TryPlanRoute(
            Pose(0, 0, 0, 0),
            new[] { Target("far", 20, 0, 0) },
            out var plan,
            allowDirectFallback: false), "un repli a été retourné malgré son interdiction");
        Require(plan is null, "un résultat ambigu subsiste");
    }

    private static void ReportsDifferentHeightFallback()
    {
        var graph = CreateGraph();
        graph.BeginSession("interchange");
        ObserveExact(graph, 0, 0, 0, 0, "interchange");
        Require(graph.TryPlanRoute(
            Pose(0, 0, 0, 1, map: "interchange"),
            new[] { Target("upper", 0, 4, 0) },
            out var plan), "aucun repli");
        Require(plan?.FallbackReason == LearnedRouteFallbackReason.TargetOnDifferentHeight,
            "le mauvais étage n'est pas expliqué");
    }

    private static void RejectsInvalidAndStalePoses()
    {
        var graph = CreateGraph();
        graph.BeginSession("customs");
        var first = graph.Observe(Pose(0, 0, 0, 2));
        Require(first.Accepted, "première pose rejetée");
        var stale = graph.Observe(Pose(2, 0, 0, 1));
        Require(stale.Status == LearnedRouteObservationStatus.RejectedStale, "pose ancienne acceptée");
        var invalid = graph.Observe(Pose(double.NaN, 0, 0, 3));
        Require(invalid.Status == LearnedRouteObservationStatus.RejectedInvalid, "NaN accepté");
        Require(graph.GetEdgeCount("customs") == 0, "des rejets ont créé une arête");
    }

    private static void PersistsAndRestoresGraph()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"raidpilot-route-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "routes.json");
        try
        {
            var graph = CreateGraph();
            graph.BeginSession("customs");
            ObserveExact(graph, 0, 0, 0, 0);
            ObserveExact(graph, 5, 0, 0, 1);
            ObserveExact(graph, 10, 0, 0, 2);
            Require(LearnedRouteGraphStore.TrySave(path, graph), "sauvegarde impossible");
            Require(LearnedRouteGraphStore.TryLoad(path, out var restored, TestOptions()), "chargement impossible");
            Require(restored is not null, "graphe restauré nul");
            Require(restored!.GetNodeCount("customs") == 3, "nœuds perdus");
            Require(restored.GetEdgeCount("customs") == 2, "arêtes perdues");
            Require(restored.TryPlanRoute(
                Pose(0, 0, 0, 3),
                new[] { Target("east", 10, 0, 0) },
                out var forward), "route restaurée absente");
            Require(forward?.Kind == LearnedRouteKind.LearnedGraph, "route restaurée invalide");
            Require(restored.TryPlanRoute(
                Pose(10, 0, 0, 3),
                new[] { Target("west", 0, 0, 0) },
                out var reverse), "repli retour absent");
            Require(reverse?.Kind == LearnedRouteKind.DirectFallback, "la persistance a inventé le sens retour");

            var observationWithoutSession = restored.Observe(Pose(12, 0, 0, 4));
            Require(observationWithoutSession.Status == LearnedRouteObservationStatus.RejectedNoSession,
                "une session de raid a été restaurée dangereusement");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static void RejectsUnsafePersistence()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"raidpilot-route-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "routes.json");
        try
        {
            var graph = CreateGraph();
            graph.BeginSession("customs");
            ObserveExact(graph, 0, 0, 0, 0);
            ObserveExact(graph, 5, 0, 0, 1);
            Require(LearnedRouteGraphStore.TrySave(path, graph), "sauvegarde impossible");

            var validJson = File.ReadAllText(path);
            File.WriteAllText(path, validJson.Replace("\"toId\":2", "\"toId\":999", StringComparison.Ordinal));
            Require(!LearnedRouteGraphStore.TryLoad(path, out _, TestOptions()),
                "une arête pendante est acceptée");

            File.WriteAllText(
                path,
                validJson.Replace("{\"schemaVersion\"", "{\"unexpected\":true,\"schemaVersion\"", StringComparison.Ordinal));
            Require(!LearnedRouteGraphStore.TryLoad(path, out _, TestOptions()),
                "un schéma inconnu est accepté");

            File.WriteAllText(path, "{not-json");
            Require(!LearnedRouteGraphStore.TryLoad(path, out _, TestOptions()),
                "du JSON corrompu est accepté");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static LearnedRouteGraph CreateGraph(LearnedRouteGraphOptions? options = null) =>
        new(options ?? TestOptions());

    private static LearnedRouteGraphOptions TestOptions() => new()
    {
        MaxMaps = 4,
        MaxNodesPerMap = 128,
        MaxEdgesPerMap = 512,
        NodeSpacingMeters = 1.0,
        MergeHorizontalRadiusMeters = 0.4,
        MergeVerticalToleranceMeters = 0.6,
        HeightBandMeters = 2.0,
        MaximumSegmentHorizontalMeters = 12.0,
        MaximumSegmentVerticalMeters = 2.0,
        MaximumSampleGap = TimeSpan.FromSeconds(5),
        MaximumLiveUncertaintyMeters = 12.0,
        StartSnapHorizontalRadiusMeters = 1.0,
        TargetSnapHorizontalRadiusMeters = 1.0,
        RouteVerticalToleranceMeters = 0.75,
        ArrivalHorizontalRadiusMeters = 0.25,
    };

    private static LearnedRoutePose Pose(
        double x,
        double y,
        double z,
        int seconds,
        string map = "customs",
        LearnedPoseSource source = LearnedPoseSource.ExactAnchor,
        double uncertainty = 0.0) =>
        new(map, Epoch.AddSeconds(seconds), x, y, z, source, uncertainty);

    private static LearnedRouteTarget Target(string id, double x, double y, double z) =>
        new(id, id, x, y, z);

    private static void ObserveExact(
        LearnedRouteGraph graph,
        double x,
        double y,
        double z,
        int seconds,
        string map = "customs")
    {
        var result = graph.Observe(Pose(x, y, z, seconds, map));
        Require(result.Accepted, $"observation {x},{y},{z} rejetée : {result.Status}");
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

    private static bool Near(double actual, double expected, double tolerance = 0.001) =>
        Math.Abs(actual - expected) <= tolerance;

    private static void Near(double actual, double expected, string label, double tolerance = 0.001)
    {
        Require(Near(actual, expected, tolerance),
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
