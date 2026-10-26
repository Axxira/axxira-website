using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>
/// Learns whether RECENT, actually-applied local visual corrections were
/// followed by accurate comparable native F8 observations. This is bounded
/// supervisory feedback, not neural training or absolute position estimation.
///
/// Only narrows the existing 4/8/12-second cadence. It never permits a
/// longer interval, fabricates native F8 coordinates, or persists images.
/// </summary>
internal sealed class CortexVisualFeedbackGate
{
    internal const int Capacity = 16;
    internal const int MinimumComparable = 4;
    private const double MaximumIntervalSeconds = 30.0;
    private const double MaximumVisualAgeSeconds = 20.0;

    private readonly Queue<bool> _badAfterVisual = new(Capacity);
    private string _map = string.Empty;
    private int _raid = -1;
    private nint _window;
    private long _lastNativeAt;
    private long _lastVisualAt;
    private int _visualCorrectionsSinceF8;
    private long _compared;
    private long _bad;
    private long _ignored;

    internal int SampleCount => _badAfterVisual.Count;
    internal long Comparisons => _compared;
    internal long BadComparisons => _bad;
    internal long Ignored => _ignored;

    // Not calibrated until at least four independently anchored intervals.
    internal bool Calibrated => SampleCount >= MinimumComparable;

    internal int MaximumIntervalSecondsAllowed
    {
        get
        {
            if (!Calibrated) return 12; // Normal V3 proof gates still apply.
            var failures = _badAfterVisual.Count(x => x);
            if (failures >= 2 && failures * 2 >= SampleCount) return 4;
            if (failures >= 1 && failures * 4 >= SampleCount) return 8;
            return 12;
        }
    }

    internal string Diagnostic =>
        Calibrated
            ? $"Retour visuel/F8 : {SampleCount} fenêtres, "
                + $"{_badAfterVisual.Count(x => x)} dérives élevées, "
                + $"plafond {MaximumIntervalSecondsAllowed} s"
            : $"Retour visuel/F8 : {SampleCount}/{MinimumComparable} "
                + "fenêtres comparables, apprentissage non calibré";

    internal void Reset()
    {
        _map = string.Empty;
        _raid = -1;
        _window = nint.Zero;
        _lastNativeAt = _lastVisualAt = 0;
        _visualCorrectionsSinceF8 = 0;
        _badAfterVisual.Clear();
        _compared = _bad = _ignored = 0;
    }

    internal void OnVisualApplied(
        long timestamp, string map, int raid, nint window,
        VisualRelocalizationDecision decision, bool fromSharedAtlas)
    {
        if (timestamp <= 0 || window == nint.Zero
            || string.IsNullOrWhiteSpace(map))
            return;
        EnsureSession(map, raid, window);

        if (_lastNativeAt <= 0 || timestamp <= _lastNativeAt
            || !decision.ShouldCorrect || fromSharedAtlas
            || decision.Source != VisualRelocalizationSource.Consensus
            || !double.IsFinite(decision.Confidence)
            || decision.Confidence < 0.90)
            return;

        // Multiple accepted frames within one F8 interval are only ONE
        // supervision sample, avoiding false pseudo-independent training.
        _lastVisualAt = timestamp;
        _visualCorrectionsSinceF8++;
    }

    internal void OnNativeF8(
        long timestamp, string map, int raid, nint window,
        PrecisionDriftSample? comparable)
    {
        if (timestamp <= 0 || window == nint.Zero
            || string.IsNullOrWhiteSpace(map))
            return;
        EnsureSession(map, raid, window);
        if (timestamp <= _lastNativeAt) return;

        var interval = _lastNativeAt <= 0
            ? double.PositiveInfinity
            : (timestamp - _lastNativeAt) / (double)Stopwatch.Frequency;
        var sinceVisual = _lastVisualAt <= 0
            ? double.PositiveInfinity
            : (timestamp - _lastVisualAt) / (double)Stopwatch.Frequency;
        if (_visualCorrectionsSinceF8 > 0)
        {
            if (comparable is { } sample
                && interval is >= 4 and <= MaximumIntervalSeconds
                && sinceVisual is >= 0 and <= MaximumVisualAgeSeconds
                && Valid(sample)
                // Audit's comparable interval must plausibly describe THIS
                // native-F8 interval; never attach a stale unrelated sample.
                && Math.Abs(sample.Seconds - interval) <= 3.0)
            {
                var bad = sample.ErrorMeters >
                    Math.Max(2.5, 2.5 * sample.ModelUncertaintyMeters)
                    || sample.HeadingErrorDegrees > 15;
                if (_badAfterVisual.Count == Capacity)
                    _badAfterVisual.Dequeue();
                _badAfterVisual.Enqueue(bad);
                _compared++;
                if (bad) _bad++;
            }
            else
            {
                _ignored++;
            }
        }

        _lastNativeAt = timestamp;
        _lastVisualAt = 0;
        _visualCorrectionsSinceF8 = 0;
    }

    private void EnsureSession(string map, int raid, nint window)
    {
        if (map == _map && raid == _raid && window == _window)
            return;
        Reset();
        _map = map;
        _raid = raid;
        _window = window;
    }

    private static bool Valid(PrecisionDriftSample s) =>
        double.IsFinite(s.ErrorMeters)
        && double.IsFinite(s.HeadingErrorDegrees)
        && double.IsFinite(s.Seconds)
        && double.IsFinite(s.ModelUncertaintyMeters)
        && s.ErrorMeters is >= 0 and <= 200
        && s.HeadingErrorDegrees is >= 0 and <= 180
        && s.Seconds is >= 4 and <= MaximumIntervalSeconds
        && s.ModelUncertaintyMeters is >= 0 and <= 200;
}
