using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RaidPilot.Overlay;

internal sealed record CollectiveAtlasStoredCandidate
{
    public CollectiveAtlasCandidate Candidate { get; init; } = new();
    public bool LocallyValidated { get; init; }
    public long LastSeenUnixMs { get; init; }
}

internal sealed record CollectiveAtlasDatabaseSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public List<CollectiveAtlasStoredCandidate> Candidates { get; init; } = [];
}

internal readonly record struct CollectiveAtlasStats(
    int CandidateCount,
    int ApprovedClusterCount,
    int MapCount,
    int LocallyValidatedCount);

internal static class CollectiveAtlasStore
{
    private const int MaximumDatabaseBytes = 64 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        MaxDepth = 32,
    };

    internal static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidPilot Suite",
        "CollectiveAtlas");

    internal static string DatabasePath => Path.Combine(RootDirectory, "atlas-v1.json");
    internal static string IdentityPath => Path.Combine(RootDirectory, "identity-v1.txt");
    internal static string SettingsPath => Path.Combine(RootDirectory, "settings-v1.json");

    internal static string GetOrCreateContributorId()
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            if (File.Exists(IdentityPath))
            {
                var existing = File.ReadAllText(IdentityPath).Trim();
                var normalized = CollectiveAtlasProtocol.NormalizeContributorId(existing);
                if (normalized.Length >= 16)
                {
                    return normalized;
                }
            }

            var random = RandomNumberGenerator.GetBytes(18);
            var id = "RPA_" + Convert.ToHexString(random);
            File.WriteAllText(IdentityPath, id, Encoding.UTF8);
            return id;
        }
        catch
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
                Environment.MachineName + "|" + Environment.UserName));
            return "RPA_" + Convert.ToHexString(hash.AsSpan(0, 18));
        }
    }

    internal static CollectiveAtlasDatabaseSnapshot Load()
    {
        try
        {
            var file = new FileInfo(DatabasePath);
            if (!file.Exists || file.Length is <= 0 or > MaximumDatabaseBytes)
            {
                return new CollectiveAtlasDatabaseSnapshot();
            }

            var bytes = File.ReadAllBytes(file.FullName);
            var snapshot = JsonSerializer.Deserialize<CollectiveAtlasDatabaseSnapshot>(
                bytes,
                JsonOptions);
            return snapshot?.SchemaVersion == 1
                && snapshot.Candidates is not null
                    ? snapshot
                    : new CollectiveAtlasDatabaseSnapshot();
        }
        catch
        {
            return new CollectiveAtlasDatabaseSnapshot();
        }
    }

    internal static bool Save(CollectiveAtlasDatabaseSnapshot snapshot)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            if (bytes.Length is <= 0 or > MaximumDatabaseBytes)
            {
                return false;
            }

            temporary = DatabasePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                32 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, DatabasePath, overwrite: true);
            temporary = null;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); } catch { }
            }
        }
    }

    internal static bool LoadShareEnabled()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return false;
            }
            using var doc = JsonDocument.Parse(File.ReadAllBytes(SettingsPath));
            return doc.RootElement.TryGetProperty("shareContributions", out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    internal static void SaveShareEnabled(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(new { shareContributions = enabled }),
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}

internal sealed class CollectiveAtlasEngine
{
    private const int MaximumCandidates = 4096;
    private const int MaximumCandidatesPerMap = 768;
    private const int MaximumApprovedEntriesPerMap = 48;

    private readonly object _sync = new();
    private readonly string _ownContributorId;
    private readonly Dictionary<string, CollectiveAtlasStoredCandidate> _candidates =
        new(StringComparer.OrdinalIgnoreCase);

    internal CollectiveAtlasEngine(
        string ownContributorId,
        CollectiveAtlasDatabaseSnapshot? snapshot = null)
    {
        _ownContributorId = CollectiveAtlasProtocol.NormalizeContributorId(ownContributorId);
        foreach (var stored in snapshot?.Candidates ?? [])
        {
            if (CollectiveAtlasProtocol.TryValidate(stored.Candidate, out var candidate))
            {
                _candidates[candidate.CandidateId] = stored with
                {
                    Candidate = candidate,
                    LastSeenUnixMs = Math.Max(stored.LastSeenUnixMs, candidate.CreatedAtUnixMs),
                };
            }
        }
        PruneCore();
    }

    internal string OwnContributorId => _ownContributorId;

    internal bool TryAdd(
        CollectiveAtlasCandidate candidate,
        bool locallyValidated,
        out bool changed)
    {
        changed = false;
        if (!CollectiveAtlasProtocol.TryValidate(candidate, out var normalized))
        {
            return false;
        }

        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_candidates.TryGetValue(normalized.CandidateId, out var existing))
            {
                var updated = existing with
                {
                    LocallyValidated = existing.LocallyValidated || locallyValidated,
                    LastSeenUnixMs = Math.Max(existing.LastSeenUnixMs, now),
                };
                changed = updated != existing;
                _candidates[normalized.CandidateId] = updated;
                return true;
            }

            _candidates[normalized.CandidateId] = new CollectiveAtlasStoredCandidate
            {
                Candidate = normalized,
                LocallyValidated = locallyValidated,
                LastSeenUnixMs = now,
            };
            changed = true;
            PruneCore();
            return true;
        }
    }

    internal bool MarkLocallyValidated(string mapId, VisualKeyframePose pose)
    {
        lock (_sync)
        {
            var changed = false;
            foreach (var pair in _candidates.ToArray())
            {
                if (!pair.Value.LocallyValidated
                    && CollectiveAtlasProtocol.PoseCompatible(
                        pair.Value.Candidate,
                        mapId,
                        pose))
                {
                    _candidates[pair.Key] = pair.Value with { LocallyValidated = true };
                    changed = true;
                }
            }
            return changed;
        }
    }

    internal CollectiveAtlasDatabaseSnapshot CreateSnapshot()
    {
        lock (_sync)
        {
            return new CollectiveAtlasDatabaseSnapshot
            {
                Candidates = _candidates.Values
                    .OrderByDescending(item => item.LastSeenUnixMs)
                    .ToList(),
            };
        }
    }

    internal CollectiveAtlasStats GetStats()
    {
        lock (_sync)
        {
            var approved = ApprovedRepresentativesCore(excludeOwn: true).Count;
            return new CollectiveAtlasStats(
                _candidates.Count,
                approved,
                _candidates.Values
                    .Select(item => item.Candidate.MapId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                _candidates.Values.Count(item => item.LocallyValidated));
        }
    }

    internal IReadOnlyList<CollectiveAtlasCandidate> GetGossipBatch(
        string mapId,
        int maximum)
    {
        maximum = Math.Clamp(maximum, 1, 32);
        lock (_sync)
        {
            return _candidates.Values
                .Where(item => string.Equals(
                    item.Candidate.MapId,
                    CollectiveAtlasProtocol.NormalizeMapId(mapId),
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.LocallyValidated)
                .ThenByDescending(item => ConsensusCountCore(item.Candidate))
                .ThenByDescending(item => item.LastSeenUnixMs)
                .Take(maximum)
                .Select(item => item.Candidate)
                .ToArray();
        }
    }

    internal VisualKeyframeDatabaseSnapshot CreateVisualSnapshot(
        VisualKeyframeDatabaseSnapshot template)
    {
        lock (_sync)
        {
            var result = new VisualKeyframeDatabaseSnapshot
            {
                FrameWidth = template.FrameWidth,
                FrameHeight = template.FrameHeight,
                GridColumns = template.GridColumns,
                GridRows = template.GridRows,
                CensusRadius = template.CensusRadius,
            };

            foreach (var mapGroup in ApprovedRepresentativesCore(excludeOwn: true)
                .Where(item => item.Candidate.Visual is not null)
                .GroupBy(item => item.Candidate.MapId, StringComparer.OrdinalIgnoreCase)
                .Take(16))
            {
                var map = new VisualKeyframeMapSnapshot { MapId = mapGroup.Key };
                foreach (var item in mapGroup.Take(MaximumApprovedEntriesPerMap))
                {
                    map.Keyframes.Add(item.Candidate.Visual!);
                }
                if (map.Keyframes.Count > 0)
                {
                    result.Maps.Add(map);
                }
            }
            return result;
        }
    }

    internal SuperKeyframeDatabaseSnapshot CreateSuperSnapshot(
        SuperKeyframeDatabaseSnapshot template)
    {
        lock (_sync)
        {
            var result = new SuperKeyframeDatabaseSnapshot
            {
                FrameWidth = template.FrameWidth,
                FrameHeight = template.FrameHeight,
            };

            foreach (var mapGroup in ApprovedRepresentativesCore(excludeOwn: true)
                .Where(item => item.Candidate.Super is not null)
                .GroupBy(item => item.Candidate.MapId, StringComparer.OrdinalIgnoreCase)
                .Take(16))
            {
                var map = new SuperKeyframeMapSnapshot { MapId = mapGroup.Key };
                foreach (var item in mapGroup.Take(MaximumApprovedEntriesPerMap))
                {
                    map.Keyframes.Add(item.Candidate.Super!);
                }
                if (map.Keyframes.Count > 0)
                {
                    result.Maps.Add(map);
                }
            }
            return result;
        }
    }

    internal int ApprovedCountForMap(string mapId)
    {
        var normalized = CollectiveAtlasProtocol.NormalizeMapId(mapId);
        lock (_sync)
        {
            return ApprovedRepresentativesCore(excludeOwn: true)
                .Count(item => string.Equals(
                    item.Candidate.MapId,
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static CollectiveAtlasCandidate? TryCreateCandidate(
        string contributorId,
        string mapId,
        VisualKeyframePose pose,
        VisualKeyframeDatabaseSnapshot lowSnapshot,
        SuperKeyframeDatabaseSnapshot superSnapshot,
        string clientVersion)
    {
        var normalizedMap = CollectiveAtlasProtocol.NormalizeMapId(mapId);
        var low = lowSnapshot.Maps
            .FirstOrDefault(map => string.Equals(
                map.MapId,
                normalizedMap,
                StringComparison.OrdinalIgnoreCase))
            ?.Keyframes
            .Where(entry => EntryNearPose(
                pose,
                entry.X,
                entry.Y,
                entry.Z,
                entry.YawDegrees))
            .OrderBy(entry => HorizontalDistance(pose, entry.X, entry.Z))
            .FirstOrDefault();

        var high = superSnapshot.Maps
            .FirstOrDefault(map => string.Equals(
                map.MapId,
                normalizedMap,
                StringComparison.OrdinalIgnoreCase))
            ?.Keyframes
            .Where(entry => EntryNearPose(
                pose,
                entry.X,
                entry.Y,
                entry.Z,
                entry.YawDegrees))
            .OrderBy(entry => HorizontalDistance(pose, entry.X, entry.Z))
            .FirstOrDefault();

        if (low is null && high is null)
        {
            return null;
        }

        return CollectiveAtlasProtocol.CreateCandidate(
            contributorId,
            normalizedMap,
            pose,
            low,
            high,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            clientVersion);
    }

    private List<CollectiveAtlasStoredCandidate> ApprovedRepresentativesCore(bool excludeOwn)
    {
        var approved = new List<CollectiveAtlasStoredCandidate>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _candidates.Values
            .OrderByDescending(item => item.LocallyValidated)
            .ThenByDescending(item => ConsensusCountCore(item.Candidate))
            .ThenByDescending(item => item.LastSeenUnixMs))
        {
            if (excludeOwn
                && string.Equals(
                    item.Candidate.ContributorId,
                    _ownContributorId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            // Remote Atlas data never bypasses multi-user consensus.
            // Local validation is useful metadata, but a single contributor
            // can never become a collective correction source by itself.
            if (ConsensusCountCore(item.Candidate)
                < CollectiveAtlasProtocol.MinimumConsensusContributors)
            {
                continue;
            }

            var cluster = ClusterKey(item.Candidate);
            if (used.Add(cluster))
            {
                approved.Add(item);
            }
        }

        return approved;
    }

    private int ConsensusCountCore(CollectiveAtlasCandidate candidate) =>
        _candidates.Values
            .Where(item => CollectiveAtlasProtocol.ClusterCompatible(
                candidate,
                item.Candidate))
            .Select(item => item.Candidate.ContributorId)
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static string ClusterKey(CollectiveAtlasCandidate candidate)
    {
        var x = (int)Math.Round(candidate.X / 3.0);
        var y = (int)Math.Round(candidate.Y / 3.0);
        var z = (int)Math.Round(candidate.Z / 3.0);
        var yaw = (int)Math.Round(
            CollectiveAtlasProtocol.Normalize360(candidate.YawDegrees) / 15.0) % 24;
        return $"{candidate.MapId}|{x}|{y}|{z}|{yaw}";
    }

    private void PruneCore()
    {
        foreach (var map in _candidates.Values
            .GroupBy(item => item.Candidate.MapId, StringComparer.OrdinalIgnoreCase)
            .ToArray())
        {
            foreach (var extra in map
                .OrderByDescending(item => item.LocallyValidated)
                .ThenByDescending(item => item.LastSeenUnixMs)
                .Skip(MaximumCandidatesPerMap))
            {
                _candidates.Remove(extra.Candidate.CandidateId);
            }
        }

        foreach (var extra in _candidates.Values
            .OrderByDescending(item => item.LocallyValidated)
            .ThenByDescending(item => item.LastSeenUnixMs)
            .Skip(MaximumCandidates)
            .ToArray())
        {
            _candidates.Remove(extra.Candidate.CandidateId);
        }
    }

    private static bool EntryNearPose(
        VisualKeyframePose pose,
        double x,
        double y,
        double z,
        double yaw) =>
        HorizontalDistance(pose, x, z) <= 1.25
        && Math.Abs(pose.Y - y) <= 1.5
        && CollectiveAtlasProtocol.AngleDistance(
            pose.YawDegrees,
            yaw) <= 7.0;

    private static double HorizontalDistance(
        VisualKeyframePose pose,
        double x,
        double z) =>
        Math.Sqrt(Math.Pow(pose.X - x, 2) + Math.Pow(pose.Z - z, 2));
}
