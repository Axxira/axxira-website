namespace RaidPilot.Overlay.Tests;

internal static class RealtimeCalibrationStoreTests
{
    private static readonly RealtimeCalibrationSource Source = new(0.416, 0.31);

    internal const int TestCount = 8;

    internal static void RunAll(ICollection<string> failures)
    {
        Run("calibration persistée et fusionnée", PersistsAndMergesOnlyLearnedValues, failures);
        Run("calibration absente ignorée", IgnoresMissingFile, failures);
        Run("calibration JSON corrompue ignorée", IgnoresMalformedJson, failures);
        Run("schéma inconnu et propriétés surprises refusés", RejectsUnknownSchemaAndProperties, failures);
        Run("sensibilités Control.ini différentes refusées", RejectsDifferentControlSensitivity, failures);
        Run("valeurs irréalistes refusées au chargement", RejectsUnrealisticLoadedValues, failures);
        Run("sauvegarde invalide préserve le fichier existant", InvalidSavePreservesExistingCalibration, failures);
        Run("remplacement atomique sans fichiers temporaires", ReplacesWithoutLeavingTemporaryFiles, failures);
    }

    private static void PersistsAndMergesOnlyLearnedValues()
    {
        WithTempFolder(folder =>
        {
            var path = Path.Combine(folder, "calibration.json");
            var saved = new RealtimePoseEstimatorSettings
            {
                HipYawDegreesPerMouseCount = 0.081,
                AdsYawDegreesPerMouseCount = 0.037,
                WalkSpeedMetersPerSecond = 2.65,
                SprintSpeedMetersPerSecond = 6.45,
                SlowSpeedMetersPerSecond = 1.15,
                BackwardSpeedMultiplier = 0.22,
                AccelerationMetersPerSecondSquared = 2.0,
            };
            var baseSettings = new RealtimePoseEstimatorSettings
            {
                BackwardSpeedMultiplier = 0.63,
                StrafeSpeedMultiplier = 0.71,
                AccelerationMetersPerSecondSquared = 9.25,
                PositionDriftMetersPerSecond = 0.123,
            };

            Require(RealtimeCalibrationStore.TrySave(path, saved, Source), "la sauvegarde devrait réussir");
            Require(
                RealtimeCalibrationStore.TryLoad(path, baseSettings, Source, out var merged),
                "la calibration sauvegardée devrait se charger");

            Near(merged.HipYawDegreesPerMouseCount, 0.081, "yaw hip");
            Near(merged.AdsYawDegreesPerMouseCount, 0.037, "yaw ADS");
            Near(merged.WalkSpeedMetersPerSecond, 2.65, "marche");
            Near(merged.SprintSpeedMetersPerSecond, 6.45, "sprint");
            Near(merged.SlowSpeedMetersPerSecond, 1.15, "marche lente");
            Near(merged.BackwardSpeedMultiplier, 0.63, "multiplicateur arrière de base");
            Near(merged.StrafeSpeedMultiplier, 0.71, "multiplicateur latéral de base");
            Near(merged.AccelerationMetersPerSecondSquared, 9.25, "accélération de base");
            Near(merged.PositionDriftMetersPerSecond, 0.123, "dérive de base");

            var json = File.ReadAllText(path);
            Require(!json.Contains("backwardSpeed", StringComparison.OrdinalIgnoreCase),
                "un paramètre non appris a été persisté");
            Require(!json.Contains("acceleration", StringComparison.OrdinalIgnoreCase),
                "l'accélération ne doit pas être persistée");
        });
    }

    private static void IgnoresMissingFile()
    {
        WithTempFolder(folder =>
        {
            var baseSettings = new RealtimePoseEstimatorSettings { WalkSpeedMetersPerSecond = 2.4 };
            var loaded = RealtimeCalibrationStore.TryLoad(
                Path.Combine(folder, "absent.json"),
                baseSettings,
                Source,
                out var merged);

            Require(!loaded, "un fichier absent ne devrait pas se charger");
            Require(merged == baseSettings, "les valeurs de base ont changé sans fichier");
        });
    }

    private static void IgnoresMalformedJson()
    {
        WithTempFolder(folder =>
        {
            var path = Path.Combine(folder, "calibration.json");
            File.WriteAllText(path, "{ ceci n'est pas du JSON");
            var baseSettings = new RealtimePoseEstimatorSettings();

            Require(!RealtimeCalibrationStore.TryLoad(path, baseSettings, Source, out var merged),
                "un JSON corrompu devrait être ignoré");
            Require(merged == baseSettings, "le JSON corrompu a modifié les valeurs de base");
        });
    }

    private static void RejectsUnknownSchemaAndProperties()
    {
        WithTempFolder(folder =>
        {
            var path = Path.Combine(folder, "calibration.json");
            var baseSettings = new RealtimePoseEstimatorSettings();
            File.WriteAllText(path, ValidJson(schemaVersion: 3));
            Require(!RealtimeCalibrationStore.TryLoad(path, baseSettings, Source, out _),
                "une version de schéma inconnue devrait être refusée");

            File.WriteAllText(path, ValidJson(extraCalibrationProperty: ",\"unexpected\":1"));
            Require(!RealtimeCalibrationStore.TryLoad(path, baseSettings, Source, out _),
                "une propriété de calibration inattendue devrait être refusée");

            File.WriteAllText(path, ValidJson(extraRootProperty: ",\"playerPosition\":42"));
            Require(!RealtimeCalibrationStore.TryLoad(path, baseSettings, Source, out _),
                "un état de raid inattendu devrait être refusé");
        });
    }

    private static void RejectsDifferentControlSensitivity()
    {
        WithTempFolder(folder =>
        {
            var path = Path.Combine(folder, "calibration.json");
            var settings = new RealtimePoseEstimatorSettings();
            Require(RealtimeCalibrationStore.TrySave(path, settings, Source),
                "la sauvegarde de référence a échoué");

            var changedHip = Source with { MouseSensitivity = Source.MouseSensitivity + 0.01 };
            Require(!RealtimeCalibrationStore.TryLoad(path, settings, changedHip, out var hipResult),
                "une calibration hip apprise avec une autre sensibilité a été réutilisée");
            Require(hipResult == settings, "un refus de source doit conserver les valeurs de base");

            var changedAds = Source with { MouseAimingSensitivity = Source.MouseAimingSensitivity + 0.01 };
            Require(!RealtimeCalibrationStore.TryLoad(path, settings, changedAds, out _),
                "une calibration ADS apprise avec une autre sensibilité a été réutilisée");
        });
    }

    private static void RejectsUnrealisticLoadedValues()
    {
        WithTempFolder(folder =>
        {
            var path = Path.Combine(folder, "calibration.json");
            var baseSettings = new RealtimePoseEstimatorSettings();
            File.WriteAllText(path, ValidJson(hipYaw: 0.75));

            Require(!RealtimeCalibrationStore.TryLoad(path, baseSettings, Source, out var merged),
                "un facteur yaw irréaliste devrait être refusé");
            Require(merged == baseSettings, "une valeur invalide a été fusionnée");

            File.WriteAllText(path, ValidJson(walkSpeed: 3.4, sprintSpeed: 3.2));
            Require(!RealtimeCalibrationStore.TryLoad(path, baseSettings, Source, out _),
                "un sprint plus lent que la marche devrait être refusé");
        });
    }

    private static void InvalidSavePreservesExistingCalibration()
    {
        WithTempFolder(folder =>
        {
            var path = Path.Combine(folder, "calibration.json");
            var valid = new RealtimePoseEstimatorSettings { HipYawDegreesPerMouseCount = 0.09 };
            Require(RealtimeCalibrationStore.TrySave(path, valid, Source), "la sauvegarde initiale a échoué");
            var before = File.ReadAllBytes(path);

            var invalid = valid with { HipYawDegreesPerMouseCount = double.NaN };
            Require(!RealtimeCalibrationStore.TrySave(path, invalid, Source),
                "une calibration NaN ne devrait pas être sauvegardée");
            Require(before.SequenceEqual(File.ReadAllBytes(path)),
                "la sauvegarde invalide a altéré le dernier fichier valide");
        });
    }

    private static void ReplacesWithoutLeavingTemporaryFiles()
    {
        WithTempFolder(folder =>
        {
            var path = Path.Combine(folder, "calibration.json");
            var first = new RealtimePoseEstimatorSettings { WalkSpeedMetersPerSecond = 2.0 };
            var second = first with { WalkSpeedMetersPerSecond = 3.0 };

            Require(RealtimeCalibrationStore.TrySave(path, first, Source), "la première sauvegarde a échoué");
            Require(RealtimeCalibrationStore.TrySave(path, second, Source), "le remplacement a échoué");
            Require(RealtimeCalibrationStore.TryLoad(path, new(), Source, out var loaded),
                "le fichier remplacé ne se charge pas");
            Near(loaded.WalkSpeedMetersPerSecond, 3.0, "valeur remplacée");
            Require(!Directory.EnumerateFiles(folder, "*.tmp").Any(),
                "un fichier temporaire est resté après la sauvegarde");
        });
    }

    private static string ValidJson(
        int schemaVersion = 2,
        double hipYaw = 0.05,
        double adsYaw = 0.025,
        double walkSpeed = 2.2,
        double sprintSpeed = 5.7,
        double slowSpeed = 1.0,
        string extraCalibrationProperty = "",
        string extraRootProperty = "")
    {
        var hip = Invariant(hipYaw);
        var ads = Invariant(adsYaw);
        var walk = Invariant(walkSpeed);
        var sprint = Invariant(sprintSpeed);
        var slow = Invariant(slowSpeed);
        return $$"""
        {"schemaVersion":{{schemaVersion}},"source":{"mouseSensitivity":0.416,"mouseAimingSensitivity":0.31},"calibration":{"hipYawDegreesPerMouseCount":{{hip}},"adsYawDegreesPerMouseCount":{{ads}},"walkSpeedMetersPerSecond":{{walk}},"sprintSpeedMetersPerSecond":{{sprint}},"slowSpeedMetersPerSecond":{{slow}}{{extraCalibrationProperty}}}{{extraRootProperty}}}
        """;
    }

    private static string Invariant(double value) =>
        value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static void WithTempFolder(Action<string> action)
    {
        var folder = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"RaidPilot-calibration-tests-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(folder);
        try
        {
            action(folder);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
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

    private static void Near(double actual, double expected, string label, double tolerance = 0.000001)
    {
        Require(Math.Abs(actual - expected) <= tolerance,
            $"{label} attendu {expected:F6}, reçu {actual:F6}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
