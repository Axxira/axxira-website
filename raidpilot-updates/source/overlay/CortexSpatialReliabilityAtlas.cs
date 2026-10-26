using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// CORTEX V4's private, coarse spatial *risk* atlas. It learns sector-level
/// reliability only from comparable native F8 errors. It can RESTRICT the
/// safe screenshot interval but can never authorize a longer one.
/// Existing VisualKeyframeStore / SuperKeyframeStore hold the actual durable
/// visual descriptors and exact F8 poses; this small atlas duplicates neither.
/// </summary>
internal sealed class CortexSpatialReliabilityAtlas
{
    internal const int MaximumCells = 512;
    internal const double CellSizeMeters = 40.0;
    private const long RetentionDays = 30;
    private readonly Dictionary<string, Cell> _cells = new(StringComparer.Ordinal);
    internal int Count => _cells.Count;
    internal long Observations { get; private set; }

    internal sealed record Cell(
        string Map, int X, int Z, int Floor,
        int Samples, int Bad, long SeenUnixSeconds);

    internal sealed record Snapshot(int SchemaVersion, List<Cell> Cells);

    internal bool Observe(string map, double x, double y, double z,
        PrecisionDriftSample sample, DateTimeOffset now)
    {
        if (!TryKey(map, x, y, z, out var key, out var sector)
            || !ValidResidual(sample) || now.ToUnixTimeSeconds() <= 0)
            return false;

        Prune(now);
        _cells.TryGetValue(key, out var old);
        var bad = sample.ErrorMeters > Math.Max(2.5, 2.5 * sample.ModelUncertaintyMeters)
            || sample.HeadingErrorDegrees > 15.0;
        var samples = Math.Min(20, (old?.Samples ?? 0) + 1);
        var mistakes = Math.Min(samples, (old?.Bad ?? 0) + (bad ? 1 : 0));
        if (old is { Samples: >= 20 })
        {
            // Forget some ancient mistakes rather than capping a sector
            // forever after one player's temporary camera calibration issue.
            mistakes = Math.Min(samples, (int)Math.Floor(old.Bad * 0.85)
                + (bad ? 1 : 0));
        }
        _cells[key] = sector with
        {
            Samples = samples, Bad = mistakes,
            SeenUnixSeconds = now.ToUnixTimeSeconds()
        };
        Observations++;
        Trim();
        return true;
    }

    internal int MaximumIntervalSeconds(string map, double x, double y,
        double z, DateTimeOffset now)
    {
        if (!TryKey(map, x, y, z, out var key, out _)
            || !_cells.TryGetValue(key, out var cell)
            || now.ToUnixTimeSeconds() - cell.SeenUnixSeconds
                > RetentionDays * 86400
            || cell.Samples < 4)
            return 12;
        if (cell.Bad >= 2) return 4;
        return cell.Bad >= 1 ? 8 : 12;
    }

    internal Snapshot Export(DateTimeOffset now)
    {
        Prune(now);
        return new(1, _cells.Values.OrderBy(x => x.Map, StringComparer.Ordinal)
            .ThenBy(x => x.X).ThenBy(x => x.Z)
            .ThenBy(x => x.Floor).ToList());
    }

    internal bool Import(Snapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1
            || snapshot.Cells is null || snapshot.Cells.Count > MaximumCells)
            return false;
        var next = new Dictionary<string, Cell>(StringComparer.Ordinal);
        foreach (var cell in snapshot.Cells)
        {
            if (cell is null || !ValidMap(cell.Map)
                || cell.X is < -500 or > 500
                || cell.Z is < -500 or > 500
                || cell.Floor is < -300 or > 300
                || cell.Samples is < 1 or > 20
                || cell.Bad < 0 || cell.Bad > cell.Samples
                || cell.SeenUnixSeconds <= 0
                || cell.SeenUnixSeconds > now.ToUnixTimeSeconds() + 300)
                return false;
            if (now.ToUnixTimeSeconds() - cell.SeenUnixSeconds
                > RetentionDays * 86400) continue;
            var key = MakeKey(cell.Map, cell.X, cell.Z, cell.Floor);
            if (!next.TryAdd(key, cell)) return false;
        }
        _cells.Clear();
        foreach (var kvp in next) _cells.Add(kvp.Key, kvp.Value);
        Observations = 0;
        return true;
    }

    private void Prune(DateTimeOffset now)
    {
        var expired = _cells.Where(p => now.ToUnixTimeSeconds()
                - p.Value.SeenUnixSeconds > RetentionDays * 86400)
            .Select(p => p.Key).ToArray();
        foreach (var key in expired) _cells.Remove(key);
    }

    private void Trim()
    {
        while (_cells.Count > MaximumCells)
        {
            var oldest = _cells.OrderBy(p => p.Value.SeenUnixSeconds)
                .ThenBy(p => p.Key, StringComparer.Ordinal).First();
            _cells.Remove(oldest.Key);
        }
    }

    private static bool ValidMap(string? map) => map is { Length: >= 2 and <= 64 }
        && map.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');

    private static bool TryKey(string map, double x, double y, double z,
        out string key, out Cell sector)
    {
        key = string.Empty;
        sector = new(map, 0, 0, 0, 0, 0, 0);
        if (!ValidMap(map) || !double.IsFinite(x) || !double.IsFinite(y)
            || !double.IsFinite(z) || Math.Abs(x) > 20000
            || Math.Abs(y) > 2400 || Math.Abs(z) > 20000)
            return false;
        var cellX = (int)Math.Floor(x / CellSizeMeters);
        var cellZ = (int)Math.Floor(z / CellSizeMeters);
        var level = (int)Math.Floor(y / 8.0);
        key = MakeKey(map, cellX, cellZ, level);
        sector = new(map, cellX, cellZ, level, 0, 0, 0);
        return true;
    }

    private static string MakeKey(string map, int x, int z, int floor) =>
        map + ":" + x + ":" + z + ":" + floor;

    private static bool ValidResidual(PrecisionDriftSample s) =>
        double.IsFinite(s.ErrorMeters) && double.IsFinite(s.HeadingErrorDegrees)
        && double.IsFinite(s.ModelUncertaintyMeters) && double.IsFinite(s.Seconds)
        && s.ErrorMeters is >= 0 and <= 200
        && s.HeadingErrorDegrees is >= 0 and <= 180
        && s.ModelUncertaintyMeters is >= 0 and <= 200
        && s.Seconds is >= 4 and <= 900;
}

/// <summary>Atomic, bounded local cache, scoped to a hashed player profile.</summary>
internal static class CortexSpatialReliabilityStore
{
    private const int MaximumFileBytes = 180_000;
    internal static string GetPath(string memberId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(memberId.Trim()));
        return Path.Combine(VisualKeyframeStore.RootDirectory,
            "cortex-spatial-risk-" + Convert.ToHexString(hash.AsSpan(0, 16))
                + ".json");
    }

    internal static bool TryLoad(CortexSpatialReliabilityAtlas atlas, string path,
        DateTimeOffset? now = null)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > MaximumFileBytes)
                return false;
            var snapshot = JsonSerializer.Deserialize<
                CortexSpatialReliabilityAtlas.Snapshot>(File.ReadAllBytes(path));
            return atlas.Import(snapshot, now ?? DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException) { return false; }
    }

    internal static bool TrySave(CortexSpatialReliabilityAtlas atlas, string path,
        DateTimeOffset? now = null)
    {
        string? temp = null;
        try
        {
            var snapshot = atlas.Export(now ?? DateTimeOffset.UtcNow);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
            if (bytes.Length is <= 0 or > MaximumFileBytes) return false;
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temp, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temp, fullPath, true);
            temp = null;
            return true;
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or JsonException) { return false; }
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
