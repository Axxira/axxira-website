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
        Run("rattrapage d'un ajout sans événement", ReconcilesMissedAppend, failures);
        Run("rattrapage d'une nouvelle session sans événement", ReconcilesMissedNewSession, failures);
        Run("nouveau raid sur la même carte", DetectsSameMapNewRaid, failures);
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
        TarkovDevQuestProviderTests.RunAll(failures);
        TarkovDevJsonCacheTests.RunAll(failures);
        QuestAdvisorTests.RunAll(failures);
        EftControlSettingsTests.RunAll(failures);
        EftGameViewSettingsTests.RunAll(failures);
        RealtimePoseEstimatorTests.RunAll(failures);
        RealtimePoseHistoryTests.RunAll(failures);
        RealtimeCalibrationStoreTests.RunAll(failures);
        RawMouseInputTests.RunAll(failures);
        RawKeyboardTimelineTests.RunAll(failures);
        RealtimeControlStateTests.RunAll(failures);
        VisualMotionGateTests.RunAll(failures);
        VisualEgomotionEstimatorTests.RunAll(failures);
        VisualYawCalibrationTests.RunAll(failures);
        VisualKeyframeRecognizerTests.RunAll(failures);
        VisualKeyframeStoreTests.RunAll(failures);
        SuperKeyframeRecognizerTests.RunAll(failures);
        VisualRelocalizationEngineV2Tests.RunAll(failures);
        LocalizationRecoveryPolicyTests.RunAll(failures);
        ScreenshotHotkeySenderTests.RunAll(failures);
        AutomaticReanchorRequestTests.RunAll(failures);
        RaidTimerTests.RunAll(failures);
        LearnedRouteGraphTests.RunAll(failures);
        LearnedRouteGraphStoreAsyncTests.RunAll(failures);
        LearnedRoutePolicyTests.RunAll(failures);
        OverlayLayoutCalculatorTests.RunAll(failures);
        OverlayPreferencesStoreTests.RunAll(failures);
        ScreenshotFolderCleanerTests.RunAll(failures);
        RaidStartObservationBufferTests.RunAll(failures);
        WebNavigationPolicyTests.RunAll(failures);
        QuestContextTests.RunAll(failures);
        AutomaticFloorTests.RunAll(failures);
        TimerReliabilityTests.RunAll(failures);
        LocalPlayerMarkerTests.RunAll(failures);
        MapQualityTests.RunAll(failures);
        SquadSyncTests.RunAll(failures);
        PrecisionEngineTests.RunAll(failures);

        const int total = 21
            + RouteGuidanceCalculatorTests.TestCount
            + ExtractionAdvisorTests.TestCount
            + TarkovDevExtractionProviderTests.TestCount
            + TarkovDevQuestProviderTests.TestCount
            + TarkovDevJsonCacheTests.TestCount
            + QuestAdvisorTests.TestCount
            + EftControlSettingsTests.TestCount
            + EftGameViewSettingsTests.TestCount
            + RealtimePoseEstimatorTests.TestCount
            + RealtimePoseHistoryTests.TestCount
            + RealtimeCalibrationStoreTests.TestCount
            + RawMouseInputTests.TestCount
            + RawKeyboardTimelineTests.TestCount
            + RealtimeControlStateTests.TestCount
            + VisualMotionGateTests.TestCount
            + VisualEgomotionEstimatorTests.TestCount
            + VisualYawCalibrationTests.TestCount
            + VisualKeyframeRecognizerTests.TestCount
            + VisualKeyframeStoreTests.TestCount
            + SuperKeyframeRecognizerTests.TestCount
            + VisualRelocalizationEngineV2Tests.TestCount
            + LocalizationRecoveryPolicyTests.TestCount
            + ScreenshotHotkeySenderTests.TestCount
            + AutomaticReanchorRequestTests.TestCount
            + RaidTimerTests.TestCount
            + LearnedRouteGraphTests.TestCount
            + LearnedRouteGraphStoreAsyncTests.TestCount
            + LearnedRoutePolicyTests.TestCount
            + OverlayLayoutCalculatorTests.TestCount
            + OverlayPreferencesStoreTests.TestCount
            + ScreenshotFolderCleanerTests.TestCount
            + RaidStartObservationBufferTests.TestCount
            + WebNavigationPolicyTests.TestCount
            + QuestContextTests.TestCount
            + AutomaticFloorTests.TestCount
            + TimerReliabilityTests.TestCount
            + LocalPlayerMarkerTests.TestCount
            + MapQualityTests.TestCount
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
