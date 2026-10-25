using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>
/// Bounded, in-memory history of locally corroborated visual landmarks.
/// Two recognition events involving the same landmark, or two descriptors
/// of essentially the same location, are NOT independent spatial evidence.
/// Existing persisted VisualKeyframeStore remains responsible for actual
/// images/descriptors. This ledger never writes coordinates or images to disk.
/// </summary>
internal sealed class CortexLandmarkMemory
{
    private const int Capacity = 48;
    private const double MaximumSameLandmarkHorizontalDeltaMeters = 1.25;
    private const double MaximumSameLandmarkVerticalDeltaMeters = 1.0;
    private const double MaximumSameLandmarkYawDeltaDegrees = 12.0;
    private const double IndependentLandmarkSeparationMeters = 3.0;
    private const double MaximumSameFloorDifferenceMeters = 2.25;
    private readonly List<LandmarkEntry> _entries = new();

    internal int Count => _entries.Count;
    internal long Repeated { get; private set; }
    internal long Conflicting { get; private set; }
    internal long Evicted { get; private set; }

    internal void Reset()
    {
        _entries.Clear();
        Repeated = 0;
        Conflicting = 0;
        Evicted = 0;
    }

    internal CortexLandmarkResult Observe(
        long timestamp, long lowKeyframeId, long superKeyframeId,
        VisualKeyframePose pose)
    {
        if (timestamp <= 0 || lowKeyframeId <= 0 || superKeyframeId <= 0
            || !IsFinite(pose))
            return CortexLandmarkResult.Invalid;

        var index = _entries.FindIndex(entry =>
            entry.LowKeyframeId == lowKeyframeId
            && entry.SuperKeyframeId == superKeyframeId);

        if (index >= 0)
        {
            var entry = _entries[index];
            if (timestamp <= entry.LastSeenAt)
                return CortexLandmarkResult.Invalid;

            var horizontal = Math.Sqrt(Math.Pow(pose.X - entry.Pose.X, 2)
                + Math.Pow(pose.Z - entry.Pose.Z, 2));
            if (horizontal > MaximumSameLandmarkHorizontalDeltaMeters
                || Math.Abs(pose.Y - entry.Pose.Y)
                    > MaximumSameLandmarkVerticalDeltaMeters
                || AngleDelta(pose.YawDegrees, entry.Pose.YawDegrees)
                    > MaximumSameLandmarkYawDeltaDegrees)
            {
                Conflicting++;
                return CortexLandmarkResult.ConflictingPose;
            }

            // Refresh recency without adding another unique landmark.
            _entries[index] = entry with { LastSeenAt = timestamp };
            Repeated++;
            return CortexLandmarkResult.Revisited;
        }

        if (_entries.Count >= Capacity)
        {
            var oldestIndex = 0;
            for (var i = 1; i < _entries.Count; i++)
            {
                if (_entries[i].LastSeenAt < _entries[oldestIndex].LastSeenAt)
                    oldestIndex = i;
            }
            _entries.RemoveAt(oldestIndex);
            Evicted++;
        }

        _entries.Add(new(lowKeyframeId, superKeyframeId, pose, timestamp));
        return CortexLandmarkResult.NewLandmark;
    }

    /// <summary>
    /// Count spatially independent, freshly validated landmarks on one floor.
    /// Repeated frames, slightly different descriptors at the same point,
    /// and appearances from another floor never increase this count.
    /// </summary>
    internal int CountRecentDistinct(long now, double seconds)
    {
        if (now <= 0 || !double.IsFinite(seconds) || seconds <= 0)
            return 0;

        var fresh = _entries
            .Where(entry => now >= entry.LastSeenAt
                && (now - entry.LastSeenAt) / (double)Stopwatch.Frequency
                    <= seconds)
            .OrderByDescending(entry => entry.LastSeenAt)
            .ToArray();
        if (fresh.Length == 0) return 0;

        var referenceFloorY = fresh[0].Pose.Y;
        var selected = new List<VisualKeyframePose>(3);
        foreach (var entry in fresh)
        {
            if (Math.Abs(entry.Pose.Y - referenceFloorY)
                > MaximumSameFloorDifferenceMeters)
                continue;

            var distinct = selected.All(existing =>
                Math.Sqrt(Math.Pow(entry.Pose.X - existing.X, 2)
                    + Math.Pow(entry.Pose.Z - existing.Z, 2))
                        >= IndependentLandmarkSeparationMeters);
            if (!distinct) continue;

            selected.Add(entry.Pose);
            if (selected.Count >= 3) break;
        }

        return selected.Count;
    }

    private static bool IsFinite(VisualKeyframePose pose) =>
        double.IsFinite(pose.X)
        && double.IsFinite(pose.Y)
        && double.IsFinite(pose.Z)
        && double.IsFinite(pose.YawDegrees);

    private static double AngleDelta(double a, double b) =>
        Math.Abs(((a - b + 540.0) % 360.0 + 360.0) % 360.0 - 180.0);

    private readonly record struct LandmarkEntry(
        long LowKeyframeId,
        long SuperKeyframeId,
        VisualKeyframePose Pose,
        long LastSeenAt);
}

internal enum CortexLandmarkResult
{
    Invalid,
    NewLandmark,
    Revisited,
    ConflictingPose,
}
