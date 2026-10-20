using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Paramètres EFT dont dépend directement la calibration de rotation. Ils
/// servent d'empreinte afin de ne jamais réutiliser un apprentissage après
/// une modification des sensibilités dans Control.ini.
/// </summary>
public readonly record struct RealtimeCalibrationSource(
    double MouseSensitivity,
    double MouseAimingSensitivity);

/// <summary>
/// Persistance locale, bornée et versionnée des cinq valeurs que
/// RaidPilot apprend pendant les recalages exacts. Aucun état de raid ni
/// paramètre non calibrable n'est écrit dans ce fichier.
/// </summary>
public static class RealtimeCalibrationStore
{
    private const int CurrentSchemaVersion = 2;
    private const long MaximumFileSizeBytes = 16 * 1024;

    /// <summary>
    /// Charge une calibration valide et la fusionne avec les autres options
    /// fournies par <paramref name="baseSettings"/>. Un fichier absent,
    /// incomplet, corrompu ou issu d'une version inconnue est simplement
    /// ignoré.
    /// </summary>
    public static bool TryLoad(
        string path,
        RealtimePoseEstimatorSettings baseSettings,
        RealtimeCalibrationSource expectedSource,
        out RealtimePoseEstimatorSettings mergedSettings)
    {
        ArgumentNullException.ThrowIfNull(baseSettings);
        mergedSettings = baseSettings;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var file = new FileInfo(fullPath);
            if (!file.Exists || file.Length is <= 0 or > MaximumFileSizeBytes)
            {
                return false;
            }

            var bytes = File.ReadAllBytes(fullPath);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4,
            });

            if (!TryReadDocument(document.RootElement, out var storedSource, out var calibration)
                || !SourcesMatch(storedSource, expectedSource))
            {
                return false;
            }

            mergedSettings = baseSettings with
            {
                HipYawDegreesPerMouseCount = calibration.HipYaw,
                AdsYawDegreesPerMouseCount = calibration.AdsYaw,
                WalkSpeedMetersPerSecond = calibration.WalkSpeed,
                SprintSpeedMetersPerSecond = calibration.SprintSpeed,
                SlowSpeedMetersPerSecond = calibration.SlowSpeed,
            };
            return true;
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            mergedSettings = baseSettings;
            return false;
        }
    }

    /// <summary>
    /// Sauvegarde les valeurs apprises dans un fichier temporaire situé sur
    /// le même volume, force son contenu sur disque, puis le remplace d'un seul
    /// mouvement lorsque le système de fichiers le permet.
    /// </summary>
    public static bool TrySave(
        string path,
        RealtimePoseEstimatorSettings settings,
        RealtimeCalibrationSource source)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(path)
            || !TryCreateCalibration(settings, out var calibration)
            || !IsValid(source))
        {
            return false;
        }

        string? temporaryPath = null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
            var json = Serialize(source, calibration);

            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            CommitTemporaryFile(temporaryPath, fullPath);

            temporaryPath = null;
            return true;
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (IsExpectedStorageFailure(exception))
                {
                    // Une sauvegarde avortée ne doit jamais faire tomber l'overlay.
                }
            }
        }
    }

    private static bool TryReadDocument(
        JsonElement root,
        out RealtimeCalibrationSource source,
        out Calibration calibration)
    {
        source = default;
        calibration = default;
        if (root.ValueKind != JsonValueKind.Object
            || !HasExactlyProperties(root, "schemaVersion", "source", "calibration")
            || !root.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var schemaVersion)
            || schemaVersion != CurrentSchemaVersion
            || !root.TryGetProperty("source", out var sourceValues)
            || sourceValues.ValueKind != JsonValueKind.Object
            || !HasExactlyProperties(
                sourceValues,
                "mouseSensitivity",
                "mouseAimingSensitivity")
            || !root.TryGetProperty("calibration", out var values)
            || values.ValueKind != JsonValueKind.Object
            || !HasExactlyProperties(
                values,
                "hipYawDegreesPerMouseCount",
                "adsYawDegreesPerMouseCount",
                "walkSpeedMetersPerSecond",
                "sprintSpeedMetersPerSecond",
                "slowSpeedMetersPerSecond"))
        {
            return false;
        }

        if (!TryReadFiniteDouble(sourceValues, "mouseSensitivity", out var mouseSensitivity)
            || !TryReadFiniteDouble(sourceValues, "mouseAimingSensitivity", out var mouseAimingSensitivity)
            || !TryReadFiniteDouble(values, "hipYawDegreesPerMouseCount", out var hipYaw)
            || !TryReadFiniteDouble(values, "adsYawDegreesPerMouseCount", out var adsYaw)
            || !TryReadFiniteDouble(values, "walkSpeedMetersPerSecond", out var walkSpeed)
            || !TryReadFiniteDouble(values, "sprintSpeedMetersPerSecond", out var sprintSpeed)
            || !TryReadFiniteDouble(values, "slowSpeedMetersPerSecond", out var slowSpeed))
        {
            return false;
        }

        var candidateSource = new RealtimeCalibrationSource(mouseSensitivity, mouseAimingSensitivity);
        var candidate = new Calibration(hipYaw, adsYaw, walkSpeed, sprintSpeed, slowSpeed);
        if (!IsValid(candidateSource) || !IsValid(candidate))
        {
            return false;
        }

        source = candidateSource;
        calibration = candidate;
        return true;
    }

    private static bool TryCreateCalibration(
        RealtimePoseEstimatorSettings settings,
        out Calibration calibration)
    {
        calibration = new Calibration(
            settings.HipYawDegreesPerMouseCount,
            settings.AdsYawDegreesPerMouseCount,
            settings.WalkSpeedMetersPerSecond,
            settings.SprintSpeedMetersPerSecond,
            settings.SlowSpeedMetersPerSecond);
        return IsValid(calibration);
    }

    private static bool IsValid(Calibration calibration)
    {
        return calibration.HipYaw is >= 0.005 and <= 0.5
            && calibration.AdsYaw is >= 0.005 and <= 0.5
            && calibration.WalkSpeed is >= 1.2 and <= 3.5
            && calibration.SprintSpeed is >= 3.5 and <= 8.0
            && calibration.SlowSpeed is >= 0.4 and <= 1.8
            && calibration.SlowSpeed <= calibration.WalkSpeed
            && calibration.WalkSpeed <= calibration.SprintSpeed;
    }

    private static bool IsValid(RealtimeCalibrationSource source)
    {
        return double.IsFinite(source.MouseSensitivity)
            && double.IsFinite(source.MouseAimingSensitivity)
            && source.MouseSensitivity is >= 0.0 and <= 10.0
            && source.MouseAimingSensitivity is >= 0.0 and <= 10.0;
    }

    private static bool SourcesMatch(
        RealtimeCalibrationSource stored,
        RealtimeCalibrationSource expected)
    {
        if (!IsValid(expected))
        {
            return false;
        }

        const double tolerance = 0.000000001;
        return Math.Abs(stored.MouseSensitivity - expected.MouseSensitivity) <= tolerance
            && Math.Abs(stored.MouseAimingSensitivity - expected.MouseAimingSensitivity) <= tolerance;
    }

    private static bool TryReadFiniteDouble(
        JsonElement parent,
        string propertyName,
        out double value)
    {
        value = 0.0;
        return parent.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static bool HasExactlyProperties(JsonElement element, params string[] expectedNames)
    {
        var remaining = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        var count = 0;
        foreach (var property in element.EnumerateObject())
        {
            count++;
            if (!remaining.Remove(property.Name))
            {
                // Refuse aussi les doublons, même si leur dernière valeur est valide.
                return false;
            }
        }

        return count == expectedNames.Length && remaining.Count == 0;
    }

    private static byte[] Serialize(
        RealtimeCalibrationSource source,
        Calibration calibration)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteStartObject("source");
            writer.WriteNumber("mouseSensitivity", source.MouseSensitivity);
            writer.WriteNumber("mouseAimingSensitivity", source.MouseAimingSensitivity);
            writer.WriteEndObject();
            writer.WriteStartObject("calibration");
            writer.WriteNumber("hipYawDegreesPerMouseCount", calibration.HipYaw);
            writer.WriteNumber("adsYawDegreesPerMouseCount", calibration.AdsYaw);
            writer.WriteNumber("walkSpeedMetersPerSecond", calibration.WalkSpeed);
            writer.WriteNumber("sprintSpeedMetersPerSecond", calibration.SprintSpeed);
            writer.WriteNumber("slowSpeedMetersPerSecond", calibration.SlowSpeed);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        // Ajoute une fin de ligne lisible sans BOM; Utf8JsonWriter a déjà
        // garanti que toutes les valeurs numériques sont du JSON strict.
        var payload = buffer.ToArray();
        Array.Resize(ref payload, payload.Length + 1);
        payload[^1] = (byte)'\n';
        return payload;
    }

    private static void CommitTemporaryFile(string temporaryPath, string destinationPath)
    {
        try
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(
                    temporaryPath,
                    destinationPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        catch (Exception exception) when (IsAtomicCommitUnavailable(exception) && File.Exists(temporaryPath))
        {
            // Certains dossiers synchronisés et certains bacs à sable
            // autorisent la création et la copie, mais refusent Move/Replace.
            // Le contenu a déjà été validé et forcé sur disque : la
            // copie est donc un repli sûr, quoique non atomique.
            File.Copy(temporaryPath, destinationPath, overwrite: true);
            File.Delete(temporaryPath);
        }
    }

    private static bool IsAtomicCommitUnavailable(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException;
    }

    private static bool IsExpectedStorageFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException
            or System.Security.SecurityException
            or ArgumentException;
    }

    private readonly record struct Calibration(
        double HipYaw,
        double AdsYaw,
        double WalkSpeed,
        double SprintSpeed,
        double SlowSpeed);
}
