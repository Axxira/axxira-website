namespace RaidPilot.Overlay;

/// <summary>
/// Retains a bounded set of very recent manual screenshots while the EFT log
/// watcher catches up with a new raid. This closes the short race where F8 can
/// be received just before the map/session line is published and would
/// otherwise be erased by the raid reset a moment later.
/// </summary>
internal sealed class RaidStartObservationBuffer
{
    private static readonly TimeSpan DefaultCarryWindow = TimeSpan.FromSeconds(8);
    private const int MaximumBufferedObservations = 4;
    private readonly long _carryWindowTicks;
    private readonly List<Entry> _entries = new(MaximumBufferedObservations);
    private long _nextSequence;

    internal RaidStartObservationBuffer(
        long frequency = 0,
        TimeSpan? carryWindow = null)
    {
        var effectiveFrequency = frequency > 0
            ? frequency
            : System.Diagnostics.Stopwatch.Frequency;
        var effectiveWindow = carryWindow ?? DefaultCarryWindow;
        if (effectiveWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(carryWindow));
        }

        _carryWindowTicks = Math.Max(
            1,
            (long)Math.Ceiling(effectiveWindow.TotalSeconds * effectiveFrequency));
    }

    internal long Remember(
        ScreenshotObservation observation,
        DateTimeOffset fileTimestampUtc,
        long captureTimestamp,
        int raidGeneration,
        long observedAtTimestamp)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observedAtTimestamp <= 0)
        {
            return 0;
        }

        var sequence = unchecked(++_nextSequence);
        if (sequence == 0)
        {
            sequence = unchecked(++_nextSequence);
        }
        _entries.Add(new Entry(
            observation,
            fileTimestampUtc,
            captureTimestamp,
            raidGeneration,
            observedAtTimestamp,
            sequence));
        if (_entries.Count > MaximumBufferedObservations)
        {
            _entries.RemoveAt(0);
        }
        return sequence;
    }

    /// <summary>
    /// Commits a buffered observation only when no raid transition consumed it
    /// during the short log-watcher grace period.
    /// </summary>
    internal bool TryTakeForStableSession(
        long sequence,
        int raidGeneration,
        out RaidStartObservation observation)
    {
        observation = default;
        if (sequence == 0)
        {
            return false;
        }

        var index = _entries.FindIndex(entry => entry.Sequence == sequence);
        if (index < 0 || _entries[index].RaidGeneration != raidGeneration)
        {
            return false;
        }

        var entry = _entries[index];
        _entries.RemoveAt(index);
        observation = new RaidStartObservation(
            entry.Observation,
            entry.FileTimestampUtc,
            entry.CaptureTimestamp);
        return true;
    }

    internal bool TryTakeForRaidTransition(
        int previousRaidGeneration,
        long transitionTimestamp,
        out RaidStartObservation observation)
    {
        observation = default;
        for (var index = 0; index < _entries.Count;)
        {
            var entry = _entries[index];
            if (entry.RaidGeneration != previousRaidGeneration
                || transitionTimestamp < entry.ObservedAtTimestamp
                || transitionTimestamp - entry.ObservedAtTimestamp > _carryWindowTicks)
            {
                _entries.RemoveAt(index);
                continue;
            }

            _entries.RemoveAt(index);
            observation = new RaidStartObservation(
                entry.Observation,
                entry.FileTimestampUtc,
                entry.CaptureTimestamp);
            return true;
        }

        return false;
    }

    internal void Clear() => _entries.Clear();

    private readonly record struct Entry(
        ScreenshotObservation Observation,
        DateTimeOffset FileTimestampUtc,
        long CaptureTimestamp,
        int RaidGeneration,
        long ObservedAtTimestamp,
        long Sequence);
}

internal readonly record struct RaidStartObservation(
    ScreenshotObservation Observation,
    DateTimeOffset FileTimestampUtc,
    long CaptureTimestamp);
