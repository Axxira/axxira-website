using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Bounded, on-device audit trail of *native* F8s, independent of raid resets.
/// No map name, game position, player ID, screenshot path, raw input or image
/// is retained. This is diagnostic evidence, never localization ground truth.
/// </summary>
internal sealed class CortexF8EvidenceJournal
{
    internal const int RetentionDays = 14;
    internal const int MaximumEvents = 256;

    internal sealed record Entry(
        long UnixSeconds,
        bool CortexV4Enabled,
        PrecisionF8Outcome Outcome,
        bool CaptureTimeResolved,
        bool PredictedPoseAvailable,
        double? ErrorMeters,
        double? IntervalSeconds);

    internal sealed record Snapshot(int SchemaVersion, List<Entry> Events,
        int RawScreenshotNotifications = 0, long LastRawScreenshotUnixSeconds = 0);

    internal sealed record Summary(
        int RetainedF8Events,
        int RawScreenshotNotifications,
        string? LastRawScreenshotUtc,
        int RetentionDays,
        int MaximumEvents,
        int WithCortexV4Enabled,
        int WithResolvedCaptureTime,
        int WithPredictedPose,
        int Comparable,
        int ComparableWithCortexV4,
        double? MeasuredMeanErrorMeters,
        double? MeasuredP90ErrorMeters,
        string? LastObservedUtc,
        string? LastOutcome,
        IReadOnlyDictionary<string, int> Outcomes,
        string Interpretation);

    private readonly List<Entry> _events = new(MaximumEvents);
    private int _rawScreenshotNotifications;
    private long _lastRawScreenshotUnixSeconds;
    internal int Count => _events.Count;

    internal void ObserveRawScreenshotNotification(DateTimeOffset utc)
    {
        var at = utc.ToUnixTimeSeconds();
        if (at <= 0) return;
        Prune(utc);
        _rawScreenshotNotifications = Math.Min(1_000_000,
            _rawScreenshotNotifications + 1);
        _lastRawScreenshotUnixSeconds = at;
    }

    internal void Observe(
        DateTimeOffset utc,
        bool cortexV4Enabled,
        PrecisionF8Outcome outcome,
        bool captureTimeResolved,
        bool predictedPoseAvailable,
        PrecisionDriftSample? sample)
    {
        if (utc.ToUnixTimeSeconds() <= 0) return;
        double? error = null;
        double? interval = null;
        if (outcome == PrecisionF8Outcome.Comparable
            && sample is { } measured
            && double.IsFinite(measured.ErrorMeters)
            && measured.ErrorMeters is >= 0 and <= 200
            && double.IsFinite(measured.Seconds)
            && measured.Seconds is >= 4 and <= 900)
        {
            error = measured.ErrorMeters;
            interval = measured.Seconds;
        }
        else if (outcome == PrecisionF8Outcome.Comparable)
        {
            // Never count a stale or invalid Last sample as a valid measurement.
            outcome = PrecisionF8Outcome.InvalidResidual;
        }

        Prune(utc);
        _events.Add(new Entry(utc.ToUnixTimeSeconds(),
            cortexV4Enabled, outcome, captureTimeResolved,
            predictedPoseAvailable, error, interval));
        Trim();
    }

    internal Summary Summarize(DateTimeOffset utc)
    {
        Prune(utc);
        var errors = _events
            .Where(e => e.Outcome == PrecisionF8Outcome.Comparable
                && e.ErrorMeters is not null)
            .Select(e => e.ErrorMeters!.Value).Order().ToArray();
        var breakdown = _events.GroupBy(e => e.Outcome.ToString())
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var last = _events.Count > 0 ? _events[^1] : null;
        return new Summary(
            _events.Count,
            _rawScreenshotNotifications,
            _lastRawScreenshotUnixSeconds <= 0 ? null
                : DateTimeOffset.FromUnixTimeSeconds(
                    _lastRawScreenshotUnixSeconds).UtcDateTime.ToString("O"),
            RetentionDays, MaximumEvents,
            _events.Count(e => e.CortexV4Enabled),
            _events.Count(e => e.CaptureTimeResolved),
            _events.Count(e => e.PredictedPoseAvailable),
            _events.Count(e => e.Outcome == PrecisionF8Outcome.Comparable),
            _events.Count(e => e.Outcome == PrecisionF8Outcome.Comparable
                && e.CortexV4Enabled),
            errors.Length == 0 ? null : errors.Average(),
            errors.Length == 0 ? null : errors[
                Math.Clamp((int)Math.Ceiling(errors.Length * .9) - 1,
                    0, errors.Length - 1)],
            last is null ? null : DateTimeOffset.FromUnixTimeSeconds(
                last.UnixSeconds).UtcDateTime.ToString("O"),
            last?.Outcome.ToString(),
            breakdown,
            "F8 events are bounded and survive raid resets. Raw PNG notifications "
                + "are separate FileSystemWatcher events (not necessarily valid "
                + "position captures). Capture time, prediction and comparison "
                + "must all be valid; "
                + "this is not a measured localization accuracy improvement.");
    }

    internal Snapshot Export(DateTimeOffset utc)
    {
        Prune(utc);
        return new Snapshot(1, _events.ToList(),
            _rawScreenshotNotifications, _lastRawScreenshotUnixSeconds);
    }

    internal bool Import(Snapshot? snapshot, DateTimeOffset utc)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1
            || snapshot.Events is null
            || snapshot.Events.Count > MaximumEvents
            || snapshot.RawScreenshotNotifications is < 0 or > 1_000_000
            || snapshot.LastRawScreenshotUnixSeconds < 0
            || snapshot.LastRawScreenshotUnixSeconds
                > utc.ToUnixTimeSeconds() + 300)
            return false;
        var events = new List<Entry>(snapshot.Events.Count);
        var now = utc.ToUnixTimeSeconds();
        long previous = 0;
        foreach (var e in snapshot.Events)
        {
            if (e is null || !Enum.IsDefined(e.Outcome)
                || e.UnixSeconds < previous || e.UnixSeconds > now + 300
                || e.ErrorMeters is { } err
                    && (!double.IsFinite(err) || err is < 0 or > 200)
                || e.IntervalSeconds is { } interval
                    && (!double.IsFinite(interval) || interval is < 4 or > 900)
                || (e.Outcome == PrecisionF8Outcome.Comparable)
                    != (e.ErrorMeters is not null && e.IntervalSeconds is not null))
                return false;
            previous = e.UnixSeconds;
            if (now - e.UnixSeconds <= RetentionDays * 86400L)
                events.Add(e);
        }
        _events.Clear();
        _events.AddRange(events);
        _lastRawScreenshotUnixSeconds = snapshot.LastRawScreenshotUnixSeconds;
        _rawScreenshotNotifications = snapshot.RawScreenshotNotifications;
        Prune(utc);
        return true;
    }

    private void Prune(DateTimeOffset utc)
    {
        var oldest = utc.ToUnixTimeSeconds() - RetentionDays * 86400L;
        _events.RemoveAll(e => e.UnixSeconds < oldest);
        if (_lastRawScreenshotUnixSeconds < oldest)
        {
            _rawScreenshotNotifications = 0;
            _lastRawScreenshotUnixSeconds = 0;
        }
    }

    private void Trim()
    {
        if (_events.Count > MaximumEvents)
            _events.RemoveRange(0, _events.Count - MaximumEvents);
    }
}

/// <summary>Atomic, profile-scoped store for privacy-minimal F8 outcomes.</summary>
internal static class CortexF8EvidenceStore
{
    private const int MaximumFileBytes = 128 * 1024;

    internal static string GetPath(string memberId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(memberId.Trim()));
        return Path.Combine(VisualKeyframeStore.RootDirectory,
            "cortex-f8-evidence-" + Convert.ToHexString(hash.AsSpan(0, 16))
                + ".json");
    }

    internal static bool TryLoad(
        CortexF8EvidenceJournal model, string path, DateTimeOffset? utc = null)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > MaximumFileBytes)
                return false;
            return model.Import(
                JsonSerializer.Deserialize<CortexF8EvidenceJournal.Snapshot>(
                    File.ReadAllBytes(path)),
                utc ?? DateTimeOffset.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool TrySave(
        CortexF8EvidenceJournal model, string path, DateTimeOffset? utc = null)
    {
        string? temp = null;
        try
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(
                model.Export(utc ?? DateTimeOffset.UtcNow));
            if (data.Length is <= 0 or > MaximumFileBytes) return false;
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temp, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(data);
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
