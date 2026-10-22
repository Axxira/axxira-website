namespace RaidPilot.Overlay.Tests;

internal static class LearnedRouteGraphStoreAsyncTests
{
    internal const int TestCount = 6;

    private static readonly DateTimeOffset Epoch =
        new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    internal static void RunAll(ICollection<string> failures)
    {
        Run("sauvegarde asynchrone restaure le graphe sans session", AsyncRoundTripOmitsSession, failures);
        Run("instantané async reste isolé des mutations suivantes", SnapshotIsCapturedBeforeBackgroundWork, failures);
        Run("annulation async préserve le fichier existant", CancellationPreservesExistingFile, failures);
        Run("sauvegardes async concurrentes restent atomiques", ConcurrentSavesRemainAtomic, failures);
        Run("échec de publication async nettoie le temporaire", FailedCommitCleansTemporaryFile, failures);
        Run("taille maximale couvre le graphe borné", FileLimitCoversBoundedGraph, failures);
    }

    private static void AsyncRoundTripOmitsSession()
    {
        var folder = CreateTestFolder();
        var path = Path.Combine(folder, "routes.json");
        try
        {
            var graph = CreateGraph("customs", 0, 4, 8);
            Require(
                LearnedRouteGraphStore.TrySaveAsync(path, graph).GetAwaiter().GetResult(),
                "la sauvegarde asynchrone a échoué");
            Require(
                LearnedRouteGraphStore.TryLoad(path, out var restored, TestOptions()),
                "le graphe asynchrone ne se recharge pas");
            Require(restored is not null, "le graphe restauré est nul");
            Require(restored!.GetNodeCount("customs") == 3, "des nœuds ont été perdus");
            Require(restored.GetEdgeCount("customs") == 2, "des arêtes ont été perdues");

            var withoutSession = restored.Observe(Pose("customs", 12, 3));
            Require(
                withoutSession.Status == LearnedRouteObservationStatus.RejectedNoSession,
                "la session active a été persistée");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    private static void SnapshotIsCapturedBeforeBackgroundWork()
    {
        var folder = CreateTestFolder();
        var path = Path.Combine(folder, "routes.json");
        try
        {
            var graph = CreateGraph("customs", 0, 4);
            var save = LearnedRouteGraphStore.TrySaveAsync(path, graph);

            var later = graph.Observe(Pose("customs", 8, 2));
            Require(later.Accepted, "la mutation témoin a été rejetée");
            Require(save.GetAwaiter().GetResult(), "la sauvegarde de l'instantané a échoué");
            Require(
                LearnedRouteGraphStore.TryLoad(path, out var restored, TestOptions()),
                "l'instantané ne se recharge pas");
            Require(restored?.GetNodeCount("customs") == 2,
                "le worker a relu le graphe après la capture");
            Require(graph.GetNodeCount("customs") == 3,
                "la mutation témoin n'a pas modifié le graphe original");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    private static void CancellationPreservesExistingFile()
    {
        var folder = CreateTestFolder();
        var path = Path.Combine(folder, "routes.json");
        try
        {
            const string sentinel = "contenu-à-préserver";
            File.WriteAllText(path, sentinel);
            var graph = CreateGraph("customs", 0, 4);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var cancelled = false;
            try
            {
                LearnedRouteGraphStore
                    .TrySaveAsync(path, graph, cancellation.Token)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Require(cancelled, "le jeton annulé n'est pas propagé");
            Require(File.ReadAllText(path) == sentinel, "le fichier existant a été modifié");
            Require(FindTemporaryFiles(folder).Length == 0, "un temporaire annulé subsiste");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    private static void ConcurrentSavesRemainAtomic()
    {
        var folder = CreateTestFolder();
        var path = Path.Combine(folder, "routes.json");
        try
        {
            var customs = CreateGraph("customs", 0, 4);
            var woods = CreateGraph("woods", 0, 4, 8);
            var saves = new[]
            {
                LearnedRouteGraphStore.TrySaveAsync(path, customs),
                LearnedRouteGraphStore.TrySaveAsync(path, woods),
            };

            Task.WhenAll(saves).GetAwaiter().GetResult();
            Require(saves.All(static save => save.Result), "une sauvegarde concurrente a échoué");
            Require(
                LearnedRouteGraphStore.TryLoad(path, out var restored, TestOptions()),
                "le fichier final est partiel ou corrompu");
            Require(restored is not null, "le graphe final est nul");

            var isWholeCustoms = restored!.GetNodeCount("customs") == 2
                && restored.GetEdgeCount("customs") == 1
                && restored.GetNodeCount("woods") == 0;
            var isWholeWoods = restored.GetNodeCount("woods") == 3
                && restored.GetEdgeCount("woods") == 2
                && restored.GetNodeCount("customs") == 0;
            Require(isWholeCustoms || isWholeWoods,
                "le fichier final mélange deux instantanés");
            Require(FindTemporaryFiles(folder).Length == 0, "un temporaire concurrent subsiste");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    private static void FailedCommitCleansTemporaryFile()
    {
        var folder = CreateTestFolder();
        var path = Path.Combine(folder, "routes.json");
        try
        {
            var original = CreateGraph("customs", 0, 4);
            Require(LearnedRouteGraphStore.TrySave(path, original), "préparation du fichier impossible");

            using (var locked = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.None))
            {
                var replacement = CreateGraph("woods", 0, 4, 8);
                Require(
                    !LearnedRouteGraphStore.TrySaveAsync(path, replacement).GetAwaiter().GetResult(),
                    "la publication verrouillée aurait dû échouer");
            }

            Require(FindTemporaryFiles(folder).Length == 0, "le temporaire d'échec subsiste");
            Require(
                LearnedRouteGraphStore.TryLoad(path, out var restored, TestOptions()),
                "l'ancien fichier valide a été endommagé");
            Require(restored?.GetNodeCount("customs") == 2,
                "la publication échouée a remplacé l'ancien graphe");
            Require(restored?.GetNodeCount("woods") == 0,
                "la publication échouée a laissé un mélange");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    private static void FileLimitCoversBoundedGraph()
    {
        const long conservativeMinimumBytes = 64L * 1024L * 1024L;
        Require(
            LearnedRouteGraphStore.MaximumFileBytes >= conservativeMinimumBytes,
            "la limite de fichier est plus petite que le graphe borné annoncé");
    }

    private static LearnedRouteGraph CreateGraph(string mapId, params double[] xCoordinates)
    {
        var graph = new LearnedRouteGraph(TestOptions());
        graph.BeginSession(mapId);
        for (var index = 0; index < xCoordinates.Length; index++)
        {
            var result = graph.Observe(Pose(mapId, xCoordinates[index], index));
            Require(result.Accepted, $"observation {index} rejetée : {result.Status}");
        }

        return graph;
    }

    private static LearnedRoutePose Pose(string mapId, double x, int seconds) =>
        new(
            mapId,
            Epoch.AddSeconds(seconds),
            x,
            0,
            0,
            LearnedPoseSource.ExactAnchor,
            PositionUncertaintyMeters: 0);

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

    private static string CreateTestFolder()
    {
        var testOutput = Path.GetDirectoryName(
            typeof(LearnedRouteGraphStoreAsyncTests).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var folder = Path.Combine(
            testOutput,
            $"raidpilot-route-store-async-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string[] FindTemporaryFiles(string folder) =>
        Directory.GetFiles(folder, ".routes.json.*.tmp", SearchOption.TopDirectoryOnly);

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
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
}
