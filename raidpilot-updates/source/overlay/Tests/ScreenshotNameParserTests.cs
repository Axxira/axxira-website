namespace RaidPilot.Overlay.Tests;

internal static class ScreenshotNameParserTests
{
    public static int Main(string[] args)
    {
        if (args.Contains("--precision-bench"))
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(PrecisionEngineTests.RunBenchmarks()));
            return 0;
        }
        var auditIndex = Array.IndexOf(args, "--ocr-audit");
        if (auditIndex >= 0)
        {
            var folder = auditIndex + 1 < args.Length ? args[auditIndex + 1] : string.Empty;
            return OcrAudit.Run(folder).GetAwaiter().GetResult();
        }
        var failures = new List<string>();
        Run("nom EFT canonique", ParsesCanonicalEftFileName, failures);
        Run("conversion quaternion vers yaw Unity", ComputesExpectedUnityYaw, failures);
        Run("yaw stable en regardant vers le haut", KeepsYawWhileLookingUp, failures);
        Run("rejet des noms sans coordonnées ou non sûrs", RejectsUnrelatedOrUnsafeNames, failures);
        Run("détection des deux formats de carte", ParsesMapLogPatterns, failures);
        Run("normalisation des variantes de carte", NormalizesMapVariants, failures);
        Run("priorité au sous-dossier Logs", ResolvesBoundedLogsFolder, failures);
        Run("détection initiale dans une queue bornée", ReadsLatestMapFromBoundedTail, failures);
        Run("ancien raid terminé reste masqué au démarrage", IgnoresCompletedRaidInBoundedTail, failures);
        Run("rattrapage d'un ajout sans événement", ReconcilesMissedAppend, failures);
        Run("rattrapage d'une nouvelle session sans événement", ReconcilesMissedNewSession, failures);
        Run("nouveau raid sur la même carte", DetectsSameMapNewRaid, failures);
        Run("entraînement répété sur la même carte après sortie", DetectsSameMapTrainingAfterExit, failures);
        Run("arrêt de la réconciliation", StopsReconciliationCleanly, failures);
        Run("routes interactives pour le GPS", UsesInteractiveMapRoutes, failures);
        Run("filtre cartographique kiosque", ValidatesKioskPresentationFiles, failures);
        Run("zone OCR du panneau d'extractions", CropsExtractionPanel, failures);
        Run("zone OCR de l'onglet quêtes", CropsQuestPanel, failures);
        Run("fusion des deux colonnes OCR", MergesExtractionPanelColumnsByRow, failures);
        Run("rattrapage de la première capture", CatchesScreenshotCreatedBeforeInitialAttachment, failures);
        Run("lecture du raccourci de position", ReadsReadyScreenshotBinding, failures);
        Run("détection du raccourci Impr. écran", FlagsSysReqForRebinding, failures);
        Run("libellé de la molette", FormatsAxisScreenshotBinding, failures);
        RouteGuidanceCalculatorTests.RunAll(failures);
        ExtractionAdvisorTests.RunAll(failures);
        TarkovDevExtractionProviderTests.RunAll(failures);
        TarkovDevMineProviderTests.RunAll(failures);
        TarkovDevBoundaryProviderTests.RunAll(failures);
        TarkovDevQuestProviderTests.RunAll(failures);
        TarkovDevJsonCacheTests.RunAll(failures);
        QuestAdvisorTests.RunAll(failures);
        EftQuestLogTests.RunAll(failures);
        EftControlSettingsTests.RunAll(failures);
        EftGameViewSettingsTests.RunAll(failures);
        RealtimePoseEstimatorTests.RunAll(failures);
        RealtimePoseHistoryTests.RunAll(failures);
        RealtimeCalibrationStoreTests.RunAll(failures);
        RawMouseInputTests.RunAll(failures);
        RawKeyboardTimelineTests.RunAll(failures);
        RealtimeControlStateTests.RunAll(failures);
        RealtimeTrackingPolicyTests.RunAll(failures);
        VisualMotionGateTests.RunAll(failures);
        PrecisionV2Tests.RunAll(failures);
        PrecisionDriftAuditTests.RunAll(failures);
        VisualEgomotionEstimatorTests.RunAll(failures);
        VisualYawCalibrationTests.RunAll(failures);
        VisualKeyframeRecognizerTests.RunAll(failures);
        VisualKeyframeStoreTests.RunAll(failures);
        SuperKeyframeRecognizerTests.RunAll(failures);
        VisualRelocalizationEngineV2Tests.RunAll(failures);
        PrecisionV4Tests.RunAll(failures);
        LocalizationRecoveryPolicyTests.RunAll(failures);
        ScreenshotHotkeySenderTests.RunAll(failures);
        AutomaticReanchorRequestTests.RunAll(failures);
        MobileExactAnchorPolicyTests.RunAll(failures);
        CortexNavigationTests.RunAll(failures);
        CortexLandmarkMemoryTests.RunAll(failures);
        StashPilotTests.RunAll(failures);
        StashPilotF8GateTests.RunAll(failures);
        StashPilotWorkspaceTests.RunAll(failures);
        RaidTimerTests.RunAll(failures);
        LearnedRouteGraphTests.RunAll(failures);
        LearnedRouteGraphStoreAsyncTests.RunAll(failures);
        LearnedRoutePolicyTests.RunAll(failures);
        OverlayLayoutCalculatorTests.RunAll(failures);
        OverlayPreferencesStoreTests.RunAll(failures);
        MapZoomPreferenceTests.RunAll(failures);
        ScreenshotFolderCleanerTests.RunAll(failures);
        RaidStartObservationBufferTests.RunAll(failures);
        WebNavigationPolicyTests.RunAll(failures);
        QuestContextTests.RunAll(failures);
        AutomaticFloorTests.RunAll(failures);
        TimerReliabilityTests.RunAll(failures);
        LocalPlayerMarkerTests.RunAll(failures);
        MapQualityTests.RunAll(failures);
        CollectiveAtlasTests.RunAll(failures);
        OverlayAutoVisibilityTests.RunAll(failures);
        SquadSyncTests.RunAll(failures);
        PrecisionEngineTests.RunAll(failures);

        const int total = 23
            + RouteGuidanceCalculatorTests.TestCount
            + ExtractionAdvisorTests.TestCount
            + TarkovDevExtractionProviderTests.TestCount
            + TarkovDevMineProviderTests.TestCount
            + TarkovDevBoundaryProviderTests.TestCount
            + TarkovDevQuestProviderTests.TestCount
            + TarkovDevJsonCacheTests.TestCount
            + QuestAdvisorTests.TestCount
            + EftQuestLogTests.TestCount
            + EftControlSettingsTests.TestCount
            + EftGameViewSettingsTests.TestCount
            + RealtimePoseEstimatorTests.TestCount
            + RealtimePoseHistoryTests.TestCount
            + RealtimeCalibrationStoreTests.TestCount
            + RawMouseInputTests.TestCount
            + RawKeyboardTimelineTests.TestCount
            + RealtimeControlStateTests.TestCount
            + RealtimeTrackingPolicyTests.TestCount
            + VisualMotionGateTests.TestCount
            + PrecisionV2Tests.TestCount
            + PrecisionDriftAuditTests.TestCount
            + VisualEgomotionEstimatorTests.TestCount
            + VisualYawCalibrationTests.TestCount
            + VisualKeyframeRecognizerTests.TestCount
            + VisualKeyframeStoreTests.TestCount
            + SuperKeyframeRecognizerTests.TestCount
            + VisualRelocalizationEngineV2Tests.TestCount
            + PrecisionV4Tests.TestCount
            + LocalizationRecoveryPolicyTests.TestCount
            + ScreenshotHotkeySenderTests.TestCount
            + AutomaticReanchorRequestTests.TestCount
            + MobileExactAnchorPolicyTests.TestCount
            + CortexNavigationTests.TestCount
            + CortexLandmarkMemoryTests.TestCount
            + StashPilotTests.TestCount
            + StashPilotF8GateTests.TestCount
            + StashPilotWorkspaceTests.TestCount
            + RaidTimerTests.TestCount
            + LearnedRouteGraphTests.TestCount
            + LearnedRouteGraphStoreAsyncTests.TestCount
            + LearnedRoutePolicyTests.TestCount
            + OverlayLayoutCalculatorTests.TestCount
            + OverlayPreferencesStoreTests.TestCount
            + MapZoomPreferenceTests.TestCount
            + ScreenshotFolderCleanerTests.TestCount
            + RaidStartObservationBufferTests.TestCount
            + WebNavigationPolicyTests.TestCount
            + QuestContextTests.TestCount
            + AutomaticFloorTests.TestCount
            + TimerReliabilityTests.TestCount
            + LocalPlayerMarkerTests.TestCount
            + MapQualityTests.TestCount
            + CollectiveAtlasTests.TestCount
            + OverlayAutoVisibilityTests.TestCount
            + SquadSyncTests.TestCount
            + PrecisionEngineTests.TestCount;

        if (failures.Count == 0)
        {
            Console.WriteLine($"{total}/{total} auto-tests réussis.");
            return 0;
        }

        Console.Error.WriteLine($"{failures.Count}/{total} auto-test(s) en échec :");
        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 1;
    }

    private static void ParsesCanonicalEftFileName()
    {
        const string fileName = "2026-01-03[16-20]_166.52, 17.87, 7.16_0.03673, 0.94566, -0.11506, 0.30189_14.37 (0).png";

        var parsed = ScreenshotNameParser.TryParse(fileName, out var observation);

        if (!parsed || observation is null)
        {
            throw new InvalidOperationException("le nom canonique n'est pas reconnu");
        }

        Require(Math.Abs(observation.X - 166.52) < 0.005, "x incorrect");
        Require(Math.Abs(observation.Y - 17.87) < 0.005, "y incorrect");
        Require(Math.Abs(observation.Z - 7.16) < 0.005, "z incorrect");
        Require(double.IsFinite(observation.YawDegrees), "yaw non fini");
    }

    private static void ComputesExpectedUnityYaw()
    {
        const string fileName = "2026-09-04[18-33]_7.86, 38.06, -27.57_-0.03307, -0.13322, 0.00384, -0.99053_21.87 (12).png";

        if (!ScreenshotNameParser.TryParse(fileName, out var observation) || observation is null)
        {
            throw new InvalidOperationException("le quaternion de référence n'est pas reconnu");
        }

        Require(observation.YawDegrees is >= 15.20 and <= 15.40,
            $"yaw EFT/TarkovMonitor attendu autour de 15.29°, reçu {observation.YawDegrees:F3}");
    }

    private static void KeepsYawWhileLookingUp()
    {
        // Rotation EFT composée : cap 30 degrés, regard 60 degrés vers
        // le haut. Le nom de screenshot encode l'axe heading avec Y/Z
        // échangés, comme TarkovMonitor.
        const string fileName = "2026-01-03[16-20]_0, 0, 0_-0.129410, 0.224144, 0.482963, 0.836516_0.png";

        if (!ScreenshotNameParser.TryParse(fileName, out var observation) || observation is null)
        {
            throw new InvalidOperationException("le quaternion incliné n'est pas reconnu");
        }

        Require(Math.Abs(observation.YawDegrees - 30.0) < 0.01,
            $"yaw attendu 30 degrés, reçu {observation.YawDegrees:F3}");
    }

    private static void RejectsUnrelatedOrUnsafeNames()
    {
        string[] fileNames =
        [
            "screenshot.png",
            "2026-01-03[16-20]_1, 2, 3_0, 0, 0, 0_14.37 (0).png",
            "..\\2026-01-03[16-20]_1, 2, 3_0, 0, 0, 1_14.37 (0).png"
        ];

        foreach (var fileName in fileNames)
        {
            Require(!ScreenshotNameParser.TryParse(fileName, out _), $"nom accepté à tort : {fileName}");
        }
    }

    private static void ParsesMapLogPatterns()
    {
        const string sceneLine = "2026-10-17 12:34:56.789|application|scene preset path:maps/rezerv_base_preset.bundle| loading";
        const string networkLine = "2026-10-17 12:35:00.000|application|TRACE-NetworkGameCreate profileStatus, Location: TarkovStreets, RaidMode: Online";

        if (!EftMapLogParser.TryParseLine(sceneLine, out var reserve) || reserve is null)
        {
            throw new InvalidOperationException("la ligne scene preset n'est pas reconnue");
        }
        Require(reserve.NormalizedName == "reserve" && reserve.Route == "/map/reserve",
            "Reserve n'est pas normalisée vers sa route tarkov.dev");

        if (!EftMapLogParser.TryParseLine(networkLine, out var streets) || streets is null)
        {
            throw new InvalidOperationException("la ligne NetworkGameCreate n'est pas reconnue");
        }
        Require(streets.NormalizedName == "streets-of-tarkov",
            "Streets n'est pas normalisée");
    }

    private static void NormalizesMapVariants()
    {
        var cases = new Dictionary<string, string>
        {
            ["factory4_night"] = "night-factory",
            ["Sandbox_high"] = "ground-zero-21",
            ["Sandbox_start"] = "ground-zero",
            ["laboratory_dark"] = "the-lab-dark",
            ["factory-3d"] = "factory",
            ["labs-2d"] = "the-lab",
            ["reserve-3d"] = "reserve",
            ["shoreline-3d-resort"] = "shoreline",
        };

        foreach (var (token, expected) in cases)
        {
            if (!EftMapLogParser.TryNormalizeMapToken(token, out var map) || map is null)
            {
                throw new InvalidOperationException($"alias non reconnu : {token}");
            }
            Require(map.NormalizedName == expected,
                $"{token} devrait produire {expected}, reçu {map.NormalizedName}");
        }

        Require(!EftMapLogParser.TryNormalizeMapToken("profil_123456", out _),
            "un identifiant inconnu ne doit jamais devenir une carte");
    }

    private static void ResolvesBoundedLogsFolder()
    {
        var root = CreateTemporaryInstall(out var logsDirectory, out _);
        try
        {
            File.WriteAllText(Path.Combine(root, "decoy_application.log"), "decoy");
            var resolved = EftMapLogWatcher.ResolveLogsDirectory(root);
            Require(string.Equals(resolved, logsDirectory, StringComparison.OrdinalIgnoreCase),
                "le résolveur n'a pas préféré <install>\\Logs");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ReadsLatestMapFromBoundedTail()
    {
        var root = CreateTemporaryInstall(out var logsDirectory, out var applicationLog);
        try
        {
            File.WriteAllText(
                applicationLog,
                "2026-10-17 12:00:00.000|application|scene preset path:maps/customs_preset.bundle\n"
                + "2026-10-17 12:01:00.000|application|TRACE-NetworkGameCreate profileStatus, Location: Woods, RaidMode: Online\n");

            var detections = new List<EftMapRoute>();
            using (var watcher = new EftMapLogWatcher(logsDirectory))
            {
                watcher.MapDetected += (_, args) => detections.Add(args.Map);
                Require(watcher.Start(), "le watcher de test n'a pas démarré");
            }

            Require(detections.Count == 1 && detections[0].NormalizedName == "woods",
                "la dernière carte de la queue bornée devrait être Woods");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void IgnoresCompletedRaidInBoundedTail()
    {
        var root = CreateTemporaryInstall(out var logsDirectory, out var applicationLog);
        try
        {
            File.WriteAllText(
                applicationLog,
                "2026-10-17 12:00:00.000|application|TRACE-NetworkGameCreate profileStatus, Location: Woods, RaidMode: Online\n"
                + "2026-10-17 12:40:00.000|Info|application|BEClient exit successfully\n");

            var detections = new List<EftMapRoute>();
            using (var watcher = new EftMapLogWatcher(logsDirectory))
            {
                watcher.MapDetected += (_, args) => detections.Add(args.Map);
                Require(watcher.Start(), "le watcher de test n'a pas démarré");
            }

            Require(
                detections.Count == 0,
                "un raid déjà terminé ne doit pas restaurer l'ancienne map au démarrage");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ReconcilesMissedNewSession()
    {
        var root = CreateTemporaryInstall(out var logsDirectory, out var applicationLog);
        using var woodsDetected = new ManualResetEventSlim();
        var woodsDetectionCount = 0;
        try
        {
            File.WriteAllText(
                applicationLog,
                "2026-10-17 12:00:00.000|application|scene preset path:maps/customs_preset.bundle\n");
            File.SetLastWriteTimeUtc(applicationLog, DateTime.UtcNow.AddMinutes(-1));

            using (var watcher = new EftMapLogWatcher(
                logsDirectory,
                TimeSpan.FromMilliseconds(50),
                watchFileSystemEvents: false))
            {
                watcher.MapDetected += (_, args) =>
                {
                    if (args.Map.NormalizedName == "woods")
                    {
                        Interlocked.Increment(ref woodsDetectionCount);
                        woodsDetected.Set();
                    }
                };
                Require(watcher.Start(), "le watcher de test n'a pas démarré");

                var nextSession = Path.Combine(logsDirectory, "log_2026.10.17_12-05-00");
                Directory.CreateDirectory(nextSession);
                File.WriteAllText(
                    Path.Combine(nextSession, "2026.10.17_12-05-00 application_000.log"),
                    "2026-10-17 12:05:00.000|application|scene preset path:maps/woods_preset.bundle\n");

                Require(woodsDetected.Wait(TimeSpan.FromSeconds(2)),
                    "la réconciliation n'a pas rattrapé la nouvelle session Woods");
                Thread.Sleep(150);
                Require(Volatile.Read(ref woodsDetectionCount) == 1,
                    "la nouvelle session Woods a été publiée plusieurs fois");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ReconcilesMissedAppend()
    {
        var root = CreateTemporaryInstall(out var logsDirectory, out var applicationLog);
        using var woodsDetected = new ManualResetEventSlim();
        var woodsDetectionCount = 0;
        try
        {
            File.WriteAllText(
                applicationLog,
                "2026-10-17 12:00:00.000|application|scene preset path:maps/laboratory_preset.bundle\n");

            using (var watcher = new EftMapLogWatcher(
                logsDirectory,
                TimeSpan.FromMilliseconds(50),
                watchFileSystemEvents: false))
            {
                watcher.MapDetected += (_, args) =>
                {
                    if (args.Map.NormalizedName == "woods")
                    {
                        Interlocked.Increment(ref woodsDetectionCount);
                        woodsDetected.Set();
                    }
                };
                Require(watcher.Start(), "le watcher de test n'a pas démarré");

                File.AppendAllText(
                    applicationLog,
                    "2026-10-17 12:01:00.000|application|scene preset path:maps/woods_preset.bundle\n");

                Require(woodsDetected.Wait(TimeSpan.FromSeconds(2)),
                    "la réconciliation n'a pas rattrapé l'ajout Woods");
                Thread.Sleep(150);
                Require(Volatile.Read(ref woodsDetectionCount) == 1,
                    "l'ajout Woods a été publié plusieurs fois");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void DetectsSameMapNewRaid()
    {
        var root = CreateTemporaryInstall(out var logsDirectory, out var applicationLog);
        using var secondRaidDetected = new ManualResetEventSlim();
        var customsDetectionCount = 0;
        try
        {
            File.WriteAllText(
                applicationLog,
                "2026-10-17 12:00:00.000|application|TRACE-NetworkGameCreate profileStatus, Location: Customs, RaidMode: Online\n");

            using (var watcher = new EftMapLogWatcher(
                logsDirectory,
                TimeSpan.FromMilliseconds(50),
                watchFileSystemEvents: false))
            {
                watcher.MapDetected += (_, args) =>
                {
                    if (args.Map.NormalizedName == "customs"
                        && Interlocked.Increment(ref customsDetectionCount) == 2)
                    {
                        secondRaidDetected.Set();
                    }
                };
                Require(watcher.Start(), "le watcher de test n'a pas démarré");

                File.AppendAllText(
                    applicationLog,
                    "2026-10-17 12:05:00.000|application|TRACE-NetworkGameCreate profileStatus, Location: Customs, RaidMode: Online\n"
                    + "2026-10-17 12:05:05.000|application|TRACE-NetworkGameCreate profileStatus, Location: Customs, RaidMode: Online\n");

                Require(secondRaidDetected.Wait(TimeSpan.FromSeconds(2)),
                    "le second raid Customs n'a pas été republié");
                Thread.Sleep(150);
                Require(Volatile.Read(ref customsDetectionCount) == 2,
                    "un doublon rapproché a été pris pour un troisième raid");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void DetectsSameMapTrainingAfterExit()
    {
        var root = CreateTemporaryInstall(out var logsDirectory, out var applicationLog);
        var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var ended = new ManualResetEventSlim();
        using var restarted = new ManualResetEventSlim();
        var mapCount = 0;
        try
        {
            File.WriteAllText(applicationLog,
                "2026-10-22 13:01:59.691|application|scene preset path:maps/customs_preset.bundle rcid:bigmap.scenespreset.asset\n");
            using var watcher = new EftMapLogWatcher(logsDirectory,
                TimeSpan.FromMilliseconds(50), watchFileSystemEvents: false);
            watcher.MapDetected += (_, args) =>
            {
                events.Enqueue(args.Map.NormalizedName);
                if (Interlocked.Increment(ref mapCount) == 2) restarted.Set();
            };
            watcher.RaidEnded += (_, _) => { events.Enqueue("end"); ended.Set(); };
            Require(watcher.Start(), "le watcher de test n'a pas démarré");
            File.AppendAllText(applicationLog,
                "2026-10-22 13:07:53.059|application|BEClient exit successfully\n");
            Require(ended.Wait(TimeSpan.FromSeconds(2)), "la sortie du premier raid n'a pas été signalée");
            // Solo training emits a scene preset, without TRACE-NetworkGameCreate.
            File.AppendAllText(applicationLog,
                "2026-10-22 13:16:37.733|application|scene preset path:maps/customs_preset.bundle rcid:bigmap.scenespreset.asset\n"
                + "2026-10-22 13:16:38.000|application|scene preset path:maps/customs_preset.bundle rcid:bigmap.scenespreset.asset\n"
                + "2026-10-22 13:17:32.958|application|GameStarted:44.46(0) real:56.18(0) diff:11.72\n");
            Require(restarted.Wait(TimeSpan.FromSeconds(2)), "le second entraînement Customs n'a pas été reconnu");
            Thread.Sleep(150);
            Require(events.SequenceEqual(new[] { "customs", "end", "customs" }),
                "la transition doit redémarrer une seule fois après la sortie");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void StopsReconciliationCleanly()
    {
        var root = CreateTemporaryInstall(out var logsDirectory, out var applicationLog);
        var woodsDetectionCount = 0;
        try
        {
            File.WriteAllText(
                applicationLog,
                "2026-10-17 12:00:00.000|application|scene preset path:maps/customs_preset.bundle\n");

            using (var watcher = new EftMapLogWatcher(
                logsDirectory,
                TimeSpan.FromMilliseconds(50),
                watchFileSystemEvents: false))
            {
                watcher.MapDetected += (_, args) =>
                {
                    if (args.Map.NormalizedName == "woods")
                    {
                        Interlocked.Increment(ref woodsDetectionCount);
                    }
                };
                Require(watcher.Start(), "le watcher de test n'a pas démarré");
                watcher.Stop();

                File.AppendAllText(
                    applicationLog,
                    "2026-10-17 12:01:00.000|application|scene preset path:maps/woods_preset.bundle\n");
                Thread.Sleep(200);
                Require(Volatile.Read(ref woodsDetectionCount) == 0,
                    "une carte a été publiée après l'arrêt du watcher");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void UsesInteractiveMapRoutes()
    {
        var cases = new Dictionary<string, string>
        {
            ["factory-3d"] = "/map/factory",
            ["interchange-2d"] = "/map/interchange",
            ["labs-2d"] = "/map/the-lab",
            ["labyrinth"] = "/map/the-labyrinth",
            ["lighthouse-2d-landscape"] = "/map/lighthouse",
            ["reserve-3d"] = "/map/reserve",
            ["shoreline-2d"] = "/map/shoreline",
            ["woods-2d"] = "/map/woods",
        };

        foreach (var (token, expectedRoute) in cases)
        {
            if (!EftMapLogParser.TryNormalizeMapToken(token, out var map) || map is null)
            {
                throw new InvalidOperationException($"route non reconnue : {token}");
            }
            Require(map.Route == expectedRoute,
                $"{token} devrait ouvrir {expectedRoute}, reçu {map.Route}");
        }
    }

    private static void ValidatesKioskPresentationFiles()
    {
        var behavior = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedBehavior.js"));
        var styles = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "InjectedStyles.css"));
        var normalizedStyles = styles.Replace("\r\n", "\n", StringComparison.Ordinal);

        Require(behavior.Contains("__raidPilotMapFilterInstalled", StringComparison.Ordinal),
            "le script n'a pas de garde idempotente");
        Require(behavior.Contains("new MutationObserver", StringComparison.Ordinal),
            "le script ne surveille pas les couches ajoutées tardivement");
        Require(behavior.Contains("__raidPilotStablePlayerFollow", StringComparison.Ordinal)
                && behavior.Contains("animate: false", StringComparison.Ordinal)
                && behavior.Contains("findLeafletMapInstance", StringComparison.Ordinal)
                && !behavior.Contains("window.L?.Map?.prototype", StringComparison.Ordinal),
            "le recentrage GPS animé peut encore faire clignoter la carte");
        Require(behavior.Contains("raidpilot-player-marker-source", StringComparison.Ordinal)
                && styles.Contains(".raidpilot-player-marker", StringComparison.Ordinal),
            "le marqueur joueur recréé n’est pas stabilisé visuellement");
        Require(behavior.Contains("__raidpilotGpsEnabled", StringComparison.Ordinal)
                && behavior.Contains("searchParams.set(\"connection\", remoteId)", StringComparison.Ordinal)
                && behavior.Contains("/^[A-HJ-NP-Z2-9]{16}$/", StringComparison.Ordinal),
            "le rechargement de la carte ne restaure pas le récepteur du point joueur");
        Require(behavior.Contains("groupNeedsLayerPolicy", StringComparison.Ordinal),
            "les reconstructions inactives des groupes peuvent relancer le filtre complet");
        Require(!behavior.Contains("new MutationObserver(scheduleLayerPolicy)", StringComparison.Ordinal),
            "chaque mouvement du marqueur relance encore toute la politique de couches");
        Require(behavior.Contains("ensurePlayerMarkerStyleObserver", StringComparison.Ordinal)
                && behavior.Contains("playerMarkerStyleObserver.observe(pane", StringComparison.Ordinal)
                && behavior.Contains("if (!targetChanged)", StringComparison.Ordinal)
                && behavior.Contains("installRetryDelay", StringComparison.Ordinal),
            "les mises à jour rapides de la carte refont encore des scans DOM/Fiber complets");
        Require(behavior.Contains("groupSelector.indeterminate", StringComparison.Ordinal),
            "le script ne traite pas les groupes partiellement actifs");
        Require(styles.Contains(":not(.map-area-label):not(.extract-icon):not(.marker)", StringComparison.Ordinal),
            "la liste blanche des marqueurs est absente");
        Require(behavior.Contains("\"spawn_boss\"", StringComparison.Ordinal)
                && behavior.Contains("\"Spawns\"", StringComparison.Ordinal)
                && behavior.Contains("hiddenSpawnLayers", StringComparison.Ordinal)
                && behavior.Contains("\"spawn_pmc\"", StringComparison.Ordinal)
                && behavior.Contains("\"spawn_scav\"", StringComparison.Ordinal)
                && behavior.Contains("\"spawn_sniper_scav\"", StringComparison.Ordinal)
                && styles.Contains("[src*=\"/spawn_boss.png\"]", StringComparison.Ordinal)
                && !styles.Contains("[src*=\"/spawn_pmc.png\"]", StringComparison.Ordinal)
                && !styles.Contains("[src*=\"/spawn_scav.png\"]", StringComparison.Ordinal),
            "les zones de boss ne sont pas isolées des autres points d'apparition");
        Require(behavior.Contains("rareLootGroupKey = \"Loose Loot\"", StringComparison.Ordinal)
                && behavior.Contains("rareLootItemKeys = new Set", StringComparison.Ordinal)
                && behavior.Contains("\"ledx-skin-transilluminator\"", StringComparison.Ordinal)
                && behavior.Contains("\"graphics-card\"", StringComparison.Ordinal)
                && behavior.Contains("\"physical-bitcoin\"", StringComparison.Ordinal)
                && behavior.Contains("itemKeys.size === 1", StringComparison.Ordinal)
                && behavior.Contains("enforceRareLootMarkers", StringComparison.Ordinal)
                && behavior.Contains("mapInstance.removeLayer(entry.layer)", StringComparison.Ordinal)
                && behavior.Contains("marker.addTo(mapInstance)", StringComparison.Ordinal)
                && behavior.Contains("rareLootMarkerClass", StringComparison.Ordinal)
                && styles.Contains(":not(.raidpilot-rare-loot-marker)", StringComparison.Ordinal)
                && styles.Contains("> .raidpilot-rare-loot-marker {", StringComparison.Ordinal)
                && normalizedStyles.Contains(
                    "#leaflet-map .leaflet-marker-pane > .raidpilot-rare-loot-marker.off-level {\n"
                    + "    z-index: initial !important;\n}",
                    StringComparison.Ordinal)
                && !styles.Contains("#leaflet-map .leaflet-marker-pane > .off-level {", StringComparison.Ordinal)
                && !styles.Contains(":not(.loot-outline)", StringComparison.Ordinal),
            "les objets rares dédiés ne restent pas visibles sur tous les étages");
        Require(styles.Contains(".leaflet-control-container", StringComparison.Ordinal),
            "les contrôles tarkov.dev ne sont pas masqués");
        Require(behavior.Contains("__raidPilotShowGuidance", StringComparison.Ordinal),
            "le HUD de guidage d’extraction n’est pas injecté");
        Require(styles.Contains("#raidpilot-guidance", StringComparison.Ordinal),
            "le style de la flèche d’extraction est absent");
        Require(behavior.Contains("__raidPilotShowLearnedRoute", StringComparison.Ordinal)
                && behavior.Contains("__raidPilotClearLearnedRoute", StringComparison.Ordinal)
                && behavior.Contains("latLngToContainerPoint([point.z, point.x])", StringComparison.Ordinal)
                && behavior.Contains("learnedRoutePointLimit = 512", StringComparison.Ordinal)
                && behavior.Contains("payload.kind !== \"learned\"", StringComparison.Ordinal)
                && behavior.Contains("requestedGeneration < learnedRouteGeneration", StringComparison.Ordinal),
            "le trajet appris n’est pas projeté ou validé de façon sûre");
        Require(styles.Contains("#leaflet-map > #raidpilot-learned-route", StringComparison.Ordinal)
                && styles.Contains("pointer-events: none !important", StringComparison.Ordinal)
                && styles.Contains("raidpilot-learned-route-confidence-medium", StringComparison.Ordinal),
            "le trajet appris peut masquer ou intercepter la carte");
        Require(behavior.Contains("__raidPilotSetRaidExtractions", StringComparison.Ordinal)
                && behavior.Contains("layer?.options?.id", StringComparison.Ordinal),
            "les extractions confirmées du raid ne sont pas appariées par ID");
        Require(behavior.Contains("matchesId || matchesLabel", StringComparison.Ordinal)
                && !behavior.Contains("if (indexedIcons.get(marker))", StringComparison.Ordinal),
            "un ID Leaflet différent bloque encore l’appariement fiable par nom");
        Require(behavior.Contains("document.body.appendChild(hud)", StringComparison.Ordinal)
                && styles.Contains("z-index: 2147483647 !important", StringComparison.Ordinal)
                && styles.Contains("position: fixed !important", StringComparison.Ordinal),
            "la flèche peut encore être perdue dans le conteneur React ou derrière la carte");
        Require(styles.Contains("raidpilot-extraction-pulse", StringComparison.Ordinal)
                && styles.Contains("raidpilot-extraction-ring", StringComparison.Ordinal)
                && styles.Contains("raidpilot-raid-extractions-confirmed", StringComparison.Ordinal)
                && behavior.Contains("matchedCount > 0", StringComparison.Ordinal),
            "le clignotement des seules sorties confirmées est absent");
        Require(behavior.Contains("__raidPilotSetRaidQuests", StringComparison.Ordinal)
                && behavior.Contains("options?.questId", StringComparison.Ordinal)
                && behavior.Contains("quest_item", StringComparison.Ordinal)
                && behavior.Contains("quest_objective", StringComparison.Ordinal)
                && behavior.Contains("matched < requested", StringComparison.Ordinal)
                && behavior.Contains("matchedQuestIds.add(questId)", StringComparison.Ordinal)
                && styles.Contains("raidpilot-quest-pulse", StringComparison.Ordinal),
            "la sélection et le clignotement des quêtes OCR sont absents");
        var questStyleStart = styles.LastIndexOf(
            "#leaflet-map .leaflet-marker-pane > .raidpilot-quest-marker {",
            StringComparison.Ordinal);
        var questStyleEnd = styles.IndexOf(
            "@keyframes raidpilot-quest-pulse",
            Math.Max(0, questStyleStart),
            StringComparison.Ordinal);
        var questStyle = questStyleStart >= 0 && questStyleEnd > questStyleStart
            ? styles[questStyleStart..questStyleEnd]
            : string.Empty;
        Require(questStyle.Length > 0
                && !questStyle.Contains("opacity: 1 !important", StringComparison.Ordinal)
                && !questStyle.Contains("filter: drop-shadow(0 0 3px #fff) drop-shadow(0 0 9px #d946ef) !important", StringComparison.Ordinal),
            "les propriétés !important empêchent encore l'animation des quêtes");
        Require(styles.Contains("--display-height: 100vh !important", StringComparison.Ordinal),
            "la hauteur de carte n’est pas stabilisée contre les rerendus GPS");
    }

    private static void CropsExtractionPanel()
    {
        var bounds = WindowsScreenshotTextReader.CalculateExtractionPanelBounds(3440, 1440);
        Require(bounds.X == 2339 && bounds.Y == 0,
            $"origine OCR inattendue : {bounds.X},{bounds.Y}");
        Require(bounds.Width == 1101 && bounds.Height == 936,
            $"taille OCR inattendue : {bounds.Width}x{bounds.Height}");
    }

    private static void CropsQuestPanel()
    {
        var ultraWide = WindowsScreenshotTextReader.CalculateQuestPanelBounds(3440, 1440);
        Require(ultraWide.X == 0 && ultraWide.Y == 0,
            $"origine OCR quêtes inattendue : {ultraWide.X},{ultraWide.Y}");
        Require(ultraWide.Width == 2500 && ultraWide.Height == 1440,
            $"taille OCR quêtes inattendue : {ultraWide.Width}x{ultraWide.Height}");

        var fullHd = WindowsScreenshotTextReader.CalculateQuestPanelBounds(1920, 1080);
        Require(fullHd.Width == 1920 && fullHd.Height == 1080,
            "un écran Full HD devrait être lu en entier");
    }

    private static void MergesExtractionPanelColumnsByRow()
    {
        WindowsScreenshotTextReader.OcrTextFragment[] fragments =
        [
            new("EXFILØI", 10, 10, 55, 14),
            new("Carrefour", 100, 11, 90, 14),
            new("EXFILØ2", 10, 38, 55, 14),
            new("Parc à remorques", 100, 39, 145, 14),
        ];

        var lines = WindowsScreenshotTextReader.MergeRowFragments(fragments);
        Require(lines.SequenceEqual(new[]
        {
            "EXFILØI Carrefour",
            "EXFILØ2 Parc à remorques",
        }), $"colonnes OCR mal fusionnées : {string.Join(" | ", lines)}");
    }

    private static void CatchesScreenshotCreatedBeforeInitialAttachment()
    {
        var root = Path.Combine(Path.GetTempPath(), $"RaidPilot-screenshots-{Guid.NewGuid():N}");
        var screenshots = Path.Combine(root, "Screenshots");
        const string fileName = "2026-10-17[19-42]_166.52, 17.87, 7.16_0.03673, 0.94566, -0.11506, 0.30189_14.37 (0).png";
        using var firstProbeCompleted = new ManualResetEventSlim();
        using var observationReceived = new ManualResetEventSlim();
        var observationCount = 0;

        Directory.CreateDirectory(root);
        try
        {
            using var watcher = new ScreenshotFolderWatcher(
                screenshots,
                TimeSpan.FromMilliseconds(50));
            watcher.StatusChanged += (_, status) =>
            {
                if (status.Contains("dossier captures attendu", StringComparison.Ordinal))
                {
                    firstProbeCompleted.Set();
                }
            };
            watcher.ObservationCreated += (_, observation) =>
            {
                if (observation.FileName == fileName)
                {
                    Interlocked.Increment(ref observationCount);
                    observationReceived.Set();
                }
            };

            watcher.Start();
            Require(firstProbeCompleted.Wait(TimeSpan.FromSeconds(2)),
                "le premier sondage n'a pas observé le dossier absent");

            Directory.CreateDirectory(screenshots);
            File.WriteAllText(Path.Combine(screenshots, fileName), string.Empty);

            Require(observationReceived.Wait(TimeSpan.FromSeconds(2)),
                "la capture créée avant l'attachement n'a pas été rattrapée");
            Thread.Sleep(150);
            Require(Volatile.Read(ref observationCount) == 1,
                "la capture initiale a été traitée plusieurs fois");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ReadsReadyScreenshotBinding()
    {
        const string json = """
            {
              "keyBindings": [
                {
                  "keyName": "MakeScreenshot",
                  "variants": [
                    { "keyCode": ["F8"] },
                    { "keyCode": [] }
                  ],
                  "pressType": "Press"
                }
              ]
            }
            """;

        var root = Path.Combine(Path.GetTempPath(), $"RaidPilot-controls-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "Control.ini");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(path, json);
            var binding = EftScreenshotBindingReader.Read(path);

            Require(binding.Status == EftScreenshotBindingStatus.Ready && binding.IsReady,
                "F8 devrait être prêt");
            Require(binding.UserLabel == "F8", $"libellé F8 inattendu : {binding.UserLabel}");
            Require(File.ReadAllText(path) == json,
                "le lecteur ne doit jamais modifier Control.ini");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void FlagsSysReqForRebinding()
    {
        const string json = """
            {
              "keyBindings": [
                {
                  "keyName": "MakeScreenshot",
                  "variants": [{ "keyCode": ["SysReq"] }]
                }
              ]
            }
            """;

        Require(EftScreenshotBindingReader.TryParse(json, out var binding),
            "le raccourci SysReq n'est pas lu");
        Require(binding.Status == EftScreenshotBindingStatus.NeedsRebind && !binding.IsReady,
            "SysReq devrait demander une reconfiguration");
        Require(binding.UserLabel.Contains("Impr. écran", StringComparison.Ordinal)
            && binding.UserLabel.Contains("à remplacer", StringComparison.Ordinal),
            $"libellé SysReq inattendu : {binding.UserLabel}");
    }

    private static void FormatsAxisScreenshotBinding()
    {
        const string json = """
            {
              "keyBindings": [
                {
                  "keyName": "MakeScreenshot",
                  "variants": [
                    {
                      "isAxis": true,
                      "keyCode": ["LeftAlt"],
                      "axisName": "Mouse Scroll",
                      "positiveAxis": true
                    }
                  ]
                }
              ]
            }
            """;

        Require(EftScreenshotBindingReader.TryParse(json, out var binding),
            "le raccourci molette n'est pas lu");
        Require(binding.IsReady, "le raccourci molette devrait être prêt");
        Require(binding.UserLabel == "Alt + Molette vers le haut",
            $"libellé molette inattendu : {binding.UserLabel}");
    }

    private static string CreateTemporaryInstall(out string logsDirectory, out string applicationLog)
    {
        var root = Path.Combine(Path.GetTempPath(), $"RaidPilot-tests-{Guid.NewGuid():N}");
        logsDirectory = Path.Combine(root, "Logs");
        var sessionDirectory = Path.Combine(logsDirectory, "log_2026.10.17_12-00-00");
        Directory.CreateDirectory(sessionDirectory);
        applicationLog = Path.Combine(sessionDirectory, "2026.10.17_12-00-00 application.log");
        File.WriteAllText(applicationLog, string.Empty);
        return root;
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
