using System.Security;
using System.Text.Json;

namespace RaidPilot.Overlay;

internal static class OverlayPreferencesStore
{
    private const string VisibleInCapturesProperty = "visibleInCaptures";
    private const string MapScalePercentProperty = "mapScalePercent";
    private const string MapCornerProperty = "mapCorner";
    private const string FreeLayoutProperty = "freeLayout";

    private sealed record Snapshot(
        bool VisibleInCaptures,
        int? MapScalePercent,
        OverlayCorner MapCorner,
        OverlayFreeLayout? FreeLayout);

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidPilot",
        "overlay-preferences.json");

    internal static bool LoadVisibleInCaptures(string? path = null) =>
        ReadSnapshot(path).VisibleInCaptures;

    internal static int? LoadMapScalePercent(string? path = null) =>
        ReadSnapshot(path).MapScalePercent;

    internal static OverlayCorner LoadMapCorner(string? path = null) =>
        ReadSnapshot(path).MapCorner;

    internal static OverlayFreeLayout? LoadFreeLayout(string? path = null) =>
        ReadSnapshot(path).FreeLayout;

    internal static bool TrySaveVisibleInCaptures(
        bool visibleInCaptures,
        string? path = null)
    {
        var current = ReadSnapshot(path);
        return TrySaveSnapshot(
            new Snapshot(
                visibleInCaptures,
                current.MapScalePercent,
                current.MapCorner,
                current.FreeLayout),
            path);
    }

    internal static bool TrySaveMapScalePercent(
        int? mapScalePercent,
        string? path = null)
    {
        if (mapScalePercent is { } percent
            && (percent < OverlayLayoutCalculator.MinimumManualScalePercent
                || percent > OverlayLayoutCalculator.MaximumManualScalePercent))
        {
            return false;
        }

        var current = ReadSnapshot(path);
        return TrySaveSnapshot(
            new Snapshot(
                current.VisibleInCaptures,
                mapScalePercent,
                current.MapCorner,
                current.FreeLayout),
            path);
    }

    internal static bool TrySaveMapCorner(
        OverlayCorner mapCorner,
        string? path = null)
    {
        if (!Enum.IsDefined(mapCorner))
        {
            return false;
        }

        var current = ReadSnapshot(path);
        return TrySaveSnapshot(
            new Snapshot(
                current.VisibleInCaptures,
                current.MapScalePercent,
                mapCorner,
                null),
            path);
    }

    internal static bool TrySaveFreeLayout(
        OverlayFreeLayout? freeLayout,
        string? path = null)
    {
        if (freeLayout is { } layout && !IsValidFreeLayout(layout))
        {
            return false;
        }

        var current = ReadSnapshot(path);
        return TrySaveSnapshot(
            new Snapshot(
                current.VisibleInCaptures,
                current.MapScalePercent,
                current.MapCorner,
                freeLayout),
            path);
    }

    private static Snapshot ReadSnapshot(string? path)
    {
        try
        {
            var settingsPath = Path.GetFullPath(path ?? DefaultPath);
            if (!File.Exists(settingsPath))
            {
                return new Snapshot(true, null, OverlayCorner.TopRight, null);
            }

            using var stream = new FileStream(
                settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Snapshot(true, null, OverlayCorner.TopRight, null);
            }

            var visible = true;
            if (document.RootElement.TryGetProperty(
                    VisibleInCapturesProperty,
                    out var visibleProperty))
            {
                visible = visibleProperty.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => true,
                };
            }

            int? mapScalePercent = null;
            if (document.RootElement.TryGetProperty(
                    MapScalePercentProperty,
                    out var scaleProperty)
                && scaleProperty.ValueKind == JsonValueKind.Number
                && scaleProperty.TryGetInt32(out var rawScale)
                && rawScale >= OverlayLayoutCalculator.MinimumManualScalePercent
                && rawScale <= OverlayLayoutCalculator.MaximumManualScalePercent)
            {
                mapScalePercent = rawScale;
            }

            var mapCorner = OverlayCorner.TopRight;
            if (document.RootElement.TryGetProperty(
                    MapCornerProperty,
                    out var cornerProperty)
                && cornerProperty.ValueKind == JsonValueKind.String
                && Enum.TryParse<OverlayCorner>(
                    cornerProperty.GetString(),
                    ignoreCase: true,
                    out var parsedCorner)
                && Enum.IsDefined(parsedCorner))
            {
                mapCorner = parsedCorner;
            }

            OverlayFreeLayout? freeLayout = null;
            if (document.RootElement.TryGetProperty(
                    FreeLayoutProperty,
                    out var freeLayoutProperty)
                && freeLayoutProperty.ValueKind == JsonValueKind.Object
                && TryReadFiniteDouble(freeLayoutProperty, "leftFraction", out var leftFraction)
                && TryReadFiniteDouble(freeLayoutProperty, "topFraction", out var topFraction)
                && TryReadFiniteDouble(freeLayoutProperty, "widthFraction", out var widthFraction)
                && TryReadFiniteDouble(freeLayoutProperty, "heightFraction", out var heightFraction))
            {
                var candidate = new OverlayFreeLayout(
                    leftFraction,
                    topFraction,
                    widthFraction,
                    heightFraction);
                if (IsValidFreeLayout(candidate))
                {
                    freeLayout = candidate;
                }
            }

            return new Snapshot(visible, mapScalePercent, mapCorner, freeLayout);
        }
        catch (Exception exception) when (IsExpectedFileError(exception))
        {
            // A missing, corrupt, or inaccessible preference must never stop
            // the overlay from starting.
            return new Snapshot(true, null, OverlayCorner.TopRight, null);
        }
    }

    private static bool TrySaveSnapshot(
        Snapshot snapshot,
        string? path)
    {
        string? temporaryPath = null;
        try
        {
            var settingsPath = Path.GetFullPath(path ?? DefaultPath);
            var directory = Path.GetDirectoryName(settingsPath)
                ?? throw new IOException("Le dossier des préférences est introuvable.");
            Directory.CreateDirectory(directory);

            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(settingsPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                using var writer = new Utf8JsonWriter(
                    stream,
                    new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                writer.WriteBoolean(
                    VisibleInCapturesProperty,
                    snapshot.VisibleInCaptures);
                if (snapshot.MapScalePercent is { } scale)
                {
                    writer.WriteNumber(MapScalePercentProperty, scale);
                }
                writer.WriteString(MapCornerProperty, snapshot.MapCorner.ToString());
                if (snapshot.FreeLayout is { } freeLayout)
                {
                    writer.WriteStartObject(FreeLayoutProperty);
                    writer.WriteNumber("leftFraction", freeLayout.LeftFraction);
                    writer.WriteNumber("topFraction", freeLayout.TopFraction);
                    writer.WriteNumber("widthFraction", freeLayout.WidthFraction);
                    writer.WriteNumber("heightFraction", freeLayout.HeightFraction);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            CommitTemporaryFile(temporaryPath, settingsPath);
            temporaryPath = null;
            return true;
        }
        catch (Exception exception) when (IsExpectedFileError(exception))
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
                catch (Exception exception) when (IsExpectedFileError(exception))
                {
                    // A failed preference write must not affect the overlay.
                }
            }
        }
    }

    private static bool TryReadFiniteDouble(
        JsonElement parent,
        string propertyName,
        out double value)
    {
        value = 0;
        return parent.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static bool IsValidFreeLayout(OverlayFreeLayout layout) =>
        double.IsFinite(layout.LeftFraction)
        && double.IsFinite(layout.TopFraction)
        && double.IsFinite(layout.WidthFraction)
        && double.IsFinite(layout.HeightFraction)
        && layout.LeftFraction is >= 0 and <= 1
        && layout.TopFraction is >= 0 and <= 1
        && layout.WidthFraction is > 0 and <= 1
        && layout.HeightFraction is > 0 and <= 1;

    private static bool IsExpectedFileError(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ArgumentException
            or NotSupportedException
            or JsonException;

    private static void CommitTemporaryFile(
        string temporaryPath,
        string destinationPath)
    {
        try
        {
            // The temporary file lives beside the destination, so Replace or
            // Move is a same-volume atomic publication on normal NTFS volumes.
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
        catch (Exception exception) when (IsAtomicCommitUnavailable(exception)
            && File.Exists(temporaryPath))
        {
            // Some synchronized folders and sandboxes reject renames. The
            // flushed, complete JSON remains safe to publish by copy there.
            File.Copy(temporaryPath, destinationPath, overwrite: true);
            File.Delete(temporaryPath);
        }
    }

    private static bool IsAtomicCommitUnavailable(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException;
}
