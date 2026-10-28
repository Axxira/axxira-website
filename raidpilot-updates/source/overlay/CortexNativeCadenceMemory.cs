using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// CORTEX V4.2: positive AND negative calibration from timestamp-comparable
/// native F8 residuals. Persists only bounded per-map counts, never coordinates,
/// images, paths, raw frames, player names or game inputs. Historical evidence
/// only shortens the cold start: every raid still needs fresh native F8 checks.
///
/// This is empirical, not an accuracy guarantee. It may permit a conservative
/// 6 or 8 s F8 interval; 12 s still requires local independent visual proof.
/// It never bypasses foreground, aiming, firing, interaction or F8 safeguards.
/// </summary>
internal sealed class CortexNativeCadenceMemory
{
    internal const int MaximumMaps = 16;
    internal const int RetentionDays = 7;
    private readonly Dictionary<string, MapEvidence> _maps =
        new(StringComparer.OrdinalIgnoreCase);
    private string _currentMap = string.Empty;
    private int _raidGeneration = -1;
    private nint _gameWindow;
    private int _currentClean;
    private long _comparedInRaid;
    private long _notComparableInRaid;
    private long _acceptedLifetime;

    internal sealed record MapEvidence(
        string Map, int Good, int Bad, long UpdatedUnixSeconds);
    internal sealed record Snapshot(int SchemaVersion, List<MapEvidence> Maps);

    internal int MapCount => _maps.Count;
    internal int CurrentClean => _currentClean;
    internal long ComparedInRaid => _comparedInRaid;
    internal long NotComparableInRaid => _notComparableInRaid;
    internal long AcceptedLifetime => _acceptedLifetime;

    internal void ResetSession()
    {
        _currentMap = string.Empty;
        _raidGeneration = -1;
        _gameWindow = nint.Zero;
        _currentClean = 0;
        _comparedInRaid = _notComparableInRaid = 0;
    }

    internal bool ObserveNative(
        string map, int raidGeneration, nint gameWindow,
        PrecisionDriftSample? sample, DateTimeOffset now)
    {
        if (!ValidMap(map) || gameWindow == nint.Zero || now.ToUnixTimeSeconds() <= 0)
            return false;

        if (_raidGeneration != raidGeneration || _gameWindow != gameWindow
            || !string.Equals(map, _currentMap, StringComparison.OrdinalIgnoreCase))
        {
            ResetSession();
            _currentMap = map;
            _raidGeneration = raidGeneration;
            _gameWindow = gameWindow;
        }

        if (sample is not { } residual || !ValidSample(residual))
        {
            _notComparableInRaid++;
            _currentClean = 0;
            return false;
        }

        _comparedInRaid++;
        var acceptable = residual.ErrorMeters <= Math.Min(1.5, 0.17 * residual.Seconds)
            && residual.HeadingErrorDegrees <= Math.Min(12.0, residual.Seconds)
            && residual.ModelUncertaintyMeters <= 2.0;
        var clearlyBad = residual.ErrorMeters >= Math.Max(2.5, 0.30 * residual.Seconds)
            || residual.HeadingErrorDegrees > 15.0;
        _currentClean = acceptable ? Math.Min(_currentClean + 1, 32) : 0;

        Prune(now);
        _maps.TryGetValue(map, out var old);
        // A neutral or uncertain sample is not a good observation.
        // Count actual high drift as negative evidence, with no fake rewards.
        if (acceptable || clearlyBad)
        {
            var good = old?.Good ?? 0;
            var bad = old?.Bad ?? 0;
            if (good + bad >= 64)
            {
                good /= 2;
                bad /= 2;
            }
            _maps[map] = new MapEvidence(
                map, good + (acceptable ? 1 : 0),
                bad + (clearlyBad ? 1 : 0), now.ToUnixTimeSeconds());
            _acceptedLifetime++;
            PruneAndTrim(now);
            return true;
        }

        return false;
    }

    internal double EligibleNativeIntervalSeconds(
        string map, int raidGeneration, nint gameWindow,
        RealtimePose pose, LocalizationHealthState health,
        CortexNavigationAssessment navigation,
        CortexDriftRiskSnapshot localDrift,
        DateTimeOffset now)
    {
        if (!string.Equals(map, _currentMap, StringComparison.OrdinalIgnoreCase)
            || _raidGeneration != raidGeneration || _gameWindow != gameWindow
            || _currentClean < 2
            || !_maps.TryGetValue(map, out var previous)
            || Expired(previous, now)
            || previous.Good < 6 || previous.Bad * 5 > previous.Good
            || navigation.Quality is CortexTrackingQuality.Degraded
                or CortexTrackingQuality.WaitingForF8
            || health is not (LocalizationHealthState.Green
                or LocalizationHealthState.Yellow)
            || !double.IsFinite(pose.PositionUncertaintyMeters)
            || !double.IsFinite(pose.YawUncertaintyDegrees)
            || pose.PositionUncertaintyMeters < 0
            || pose.YawUncertaintyDegrees < 0
            || pose.PositionUncertaintyMeters >= 1.35
            || pose.YawUncertaintyDegrees >= 10.0
            || localDrift.Calibrated && localDrift.MaxIntervalSeconds <= 4)
            return 4;

        // Never jump straight to 8 s from yesterday's measurements: at
        // least FOUR clean independent native checks are needed this raid.
        if (previous.Good >= 14 && previous.Bad * 8 <= previous.Good
            && _currentClean >= 4
            && (!localDrift.Calibrated
                || (localDrift.MaxIntervalSeconds >= 8
                    && localDrift.EstimatedErrorAt8Meters <= 1.35
                    && localDrift.EstimatedYawAt8Degrees <= 10)))
            return 8;

        return 6;
    }

    internal string Diagnostic(string map, DateTimeOffset now)
    {
        var history = _maps.TryGetValue(map, out var e) && !Expired(e, now)
            ? $"historique fiable {e.Good}, forte dérive {e.Bad}"
            : "aucun historique fiable pour cette carte";
        return $"F8 adaptatif natif : {history}, bonnes mesures consécutives "
            + $"{_currentClean}, F8 comparables ce raid {_comparedInRaid}, "
            + $"non comparables {_notComparableInRaid}. "
            + "Palier 6/8 s seulement si erreur, cap et incertitude restent faibles.";
    }

    internal Snapshot Export(DateTimeOffset now)
    {
        PruneAndTrim(now);
        return new(1, _maps.Values.OrderBy(x => x.Map, StringComparer.Ordinal)
            .ToList());
    }

    internal bool Import(Snapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1
            || snapshot.Maps is null || snapshot.Maps.Count > MaximumMaps)
            return false;
        var next = new Dictionary<string, MapEvidence>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Maps)
        {
            if (entry is null || !ValidMap(entry.Map)
                || entry.Good is < 0 or > 64 || entry.Bad is < 0 or > 64
                || entry.Good + entry.Bad > 64
                || entry.UpdatedUnixSeconds <= 0
                || entry.UpdatedUnixSeconds > now.ToUnixTimeSeconds() + 300)
                return false;
            if (Expired(entry, now)) continue;
            if (!next.TryAdd(entry.Map, entry)) return false;
        }

        _maps.Clear();
        foreach (var entry in next) _maps.Add(entry.Key, entry.Value);
        ResetSession();
        return true;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _maps.Where(pair => Expired(pair.Value, now))
            .Select(pair => pair.Key).ToArray())
            _maps.Remove(key);
    }

    private void PruneAndTrim(DateTimeOffset now)
    {
        Prune(now);
        while (_maps.Count > MaximumMaps)
        {
            var oldest = _maps.OrderBy(e => e.Value.UpdatedUnixSeconds)
                .ThenBy(e => e.Key, StringComparer.Ordinal).First().Key;
            _maps.Remove(oldest);
        }
    }

    private static bool Expired(MapEvidence e, DateTimeOffset now) =>
        now.ToUnixTimeSeconds() - e.UpdatedUnixSeconds > RetentionDays * 86400L;

    private static bool ValidMap(string? map) =>
        map is { Length: >= 2 and <= 64 }
        && map.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool ValidSample(PrecisionDriftSample s) =>
        s.PrecisionV2Enabled
        && double.IsFinite(s.ErrorMeters) && double.IsFinite(s.HeadingErrorDegrees)
        && double.IsFinite(s.Seconds) && double.IsFinite(s.ModelUncertaintyMeters)
        && s.ErrorMeters is >= 0 and <= 200
        && s.HeadingErrorDegrees is >= 0 and <= 180
        && s.Seconds is >= 4 and <= 30
        && s.ModelUncertaintyMeters is >= 0 and <= 200;
}

/// <summary>Small atomic local store, scoped to a hashed profile identity.</summary>
internal static class CortexNativeCadenceStore
{
    private const int MaximumBytes = 16 * 1024;

    internal static string GetPath(string memberId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(memberId.Trim()));
        return Path.Combine(VisualKeyframeStore.RootDirectory,
            "cortex-native-f8-" + Convert.ToHexString(hash.AsSpan(0, 16))
                + ".json");
    }

    internal static bool TryLoad(CortexNativeCadenceMemory model, string path,
        DateTimeOffset? now = null)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > MaximumBytes)
                return false;
            return model.Import(
                JsonSerializer.Deserialize<CortexNativeCadenceMemory.Snapshot>(
                    File.ReadAllBytes(path)),
                now ?? DateTimeOffset.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool TrySave(CortexNativeCadenceMemory model, string path,
        DateTimeOffset? now = null)
    {
        string? temp = null;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                model.Export(now ?? DateTimeOffset.UtcNow));
            if (bytes.Length is <= 0 or > MaximumBytes) return false;
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temp, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temp, full, overwrite: true);
            temp = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or JsonException)
        {
            return false;
        }
        finally
        {
            if (temp is not null)
            {
                try { File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
