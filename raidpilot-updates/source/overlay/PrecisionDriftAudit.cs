using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>
/// Purely local, bounded F8-vs-prediction residual. Stores errors and durations
/// only: no absolute coordinates, screenshot paths, keys or mouse samples.
/// Intervals with unknown capture timing or interrupted mode are not compared.
/// </summary>
internal readonly record struct PrecisionDriftSample(
    bool PrecisionV2Enabled,
    double ErrorMeters,
    double HeadingErrorDegrees,
    double Seconds,
    double ModelUncertaintyMeters,
    bool PrecisionV4Enabled = false);

/// <summary>Why an exact F8 could or could not be compared to a live pose.</summary>
internal enum PrecisionF8Outcome
{
    FirstAnchor,
    Comparable,
    MissingCaptureTimestamp,
    NonMonotonicCaptureTimestamp,
    Interrupted,
    ModeChanged,
    MissingPrediction,
    OutOfRangeInterval,
    InvalidGeometry,
    InvalidResidual
}

internal sealed class PrecisionDriftAudit
{
    private const int Capacity = 32;
    private const double MinimumSeconds = 4.0;
    private const double MaximumSeconds = 900.0;
    private readonly Queue<PrecisionDriftSample> _samples = new(Capacity);
    private long _lastAnchorAt;
    private bool _lastMode;
    private bool _lastV4Mode;
    private bool _interrupted;
    private int _ignored;

    internal PrecisionF8Outcome LastOutcome { get; private set; } = PrecisionF8Outcome.FirstAnchor;
    internal int Count => _samples.Count;
    internal int Ignored => _ignored;
    internal IReadOnlyList<PrecisionDriftSample> Samples => _samples.ToArray();
    internal PrecisionDriftSample? Last => _samples.Count == 0 ? null : _samples.Last();

    internal void Reset()
    {
        _samples.Clear();
        _lastAnchorAt = 0;
        _lastMode = false;
        _lastV4Mode = false;
        _interrupted = false;
        _ignored = 0;
        LastOutcome = PrecisionF8Outcome.FirstAnchor;
    }

    internal void MarkInterrupted() => _interrupted = true;

    internal bool ObserveExactAnchor(
        ScreenshotObservation observation,
        RealtimePose? predictedAtCapture,
        long captureAt,
        bool precisionV2Enabled,
        bool precisionV4Enabled = false)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var previous = _lastAnchorAt;
        var previousMode = _lastMode;
        var previousV4Mode = _lastV4Mode;
        var wasInterrupted = _interrupted;
        _lastAnchorAt = captureAt > 0 ? captureAt : 0;
        _lastMode = precisionV2Enabled;
        _lastV4Mode = precisionV4Enabled;
        _interrupted = false;

        // Record one explicit outcome per native F8. The normal conservative
        // comparison rules are unchanged; the diagnostics now explain zeros.
        if (captureAt <= 0)
            LastOutcome = PrecisionF8Outcome.MissingCaptureTimestamp;
        else if (previous <= 0)
            LastOutcome = PrecisionF8Outcome.FirstAnchor;
        else if (captureAt <= previous)
            LastOutcome = PrecisionF8Outcome.NonMonotonicCaptureTimestamp;
        else if (wasInterrupted)
            LastOutcome = PrecisionF8Outcome.Interrupted;
        else if (previousMode != precisionV2Enabled
            || previousV4Mode != precisionV4Enabled)
            LastOutcome = PrecisionF8Outcome.ModeChanged;
        else if (predictedAtCapture is null)
            LastOutcome = PrecisionF8Outcome.MissingPrediction;
        else
            LastOutcome = PrecisionF8Outcome.Comparable;

        if (LastOutcome != PrecisionF8Outcome.Comparable)
        {
            if (previous > 0) _ignored++;
            return false;
        }

        var predicted = predictedAtCapture!.Value;
        var seconds = (captureAt - previous) / (double)Stopwatch.Frequency;
        if (seconds is < MinimumSeconds or > MaximumSeconds)
        {
            LastOutcome = PrecisionF8Outcome.OutOfRangeInterval;
            _ignored++;
            return false;
        }

        if (!double.IsFinite(predicted.X) || !double.IsFinite(predicted.Z)
            || !double.IsFinite(predicted.YawDegrees)
            || !double.IsFinite(predicted.PositionUncertaintyMeters)
            || !double.IsFinite(observation.X) || !double.IsFinite(observation.Z)
            || !double.IsFinite(observation.YawDegrees)
            || predicted.PositionUncertaintyMeters < 0)
        {
            LastOutcome = PrecisionF8Outcome.InvalidGeometry;
            _ignored++;
            return false;
        }

        var dx = observation.X - predicted.X;
        var dz = observation.Z - predicted.Z;
        var meters = Math.Sqrt(dx * dx + dz * dz);
        var heading = Math.Abs(NormalizeYaw(observation.YawDegrees - predicted.YawDegrees));
        if (!double.IsFinite(meters) || !double.IsFinite(heading) || meters > 100_000)
        {
            LastOutcome = PrecisionF8Outcome.InvalidResidual;
            _ignored++;
            return false;
        }
        if (_samples.Count == Capacity) _samples.Dequeue();
        _samples.Enqueue(new(
            precisionV2Enabled,
            meters,
            heading,
            seconds,
            predicted.PositionUncertaintyMeters,
            precisionV4Enabled));
        return true;
    }

    internal string Describe()
    {
        var v1 = _samples.Where(x => !x.PrecisionV2Enabled && !x.PrecisionV4Enabled).ToArray();
        var v2 = _samples.Where(x => x.PrecisionV2Enabled && !x.PrecisionV4Enabled).ToArray();
        var v4 = _samples.Where(x => x.PrecisionV4Enabled).ToArray();
        return $"F8 réels comparables (mémoire seulement) : {_samples.Count}, "
            + $"intervalles écartés : {_ignored}\n"
            + DescribeMode("V1", v1) + "\n" + DescribeMode("V2", v2)
            + "\n" + DescribeMode("V4", v4)
            + "\nV4 par durée : " + DescribeWindow(v4, 4, 12, "4-12 s")
            + " · " + DescribeWindow(v4, 12, 25, "12-25 s")
            + " · " + DescribeWindow(v4, 25, 60, "25-60 s")
            + "\nComparaison indicative : durées observées et conditions peuvent différer.";
    }

    private static string DescribeMode(string name, PrecisionDriftSample[] samples)
    {
        if (samples.Length == 0) return name + " : aucune mesure fiable";
        var errors = samples.Select(s => s.ErrorMeters).Order().ToArray();
        var yaw = samples.Select(s => s.HeadingErrorDegrees).Order().ToArray();
        var minutes = samples.Select(s => s.Seconds).Order().ToArray();
        return $"{name} ({samples.Length}) : médiane {Median(errors):F1} m, "
            + $"P90 {Percentile90(errors):F1} m, cap médian {Median(yaw):F1}°, "
            + $"intervalle médian {Median(minutes):F0} s";
    }

    private static string DescribeWindow(
        PrecisionDriftSample[] samples, double minimum, double maximum, string label)
    {
        var errors = samples.Where(s => s.Seconds >= minimum && s.Seconds < maximum)
            .Select(s => s.ErrorMeters).Order().ToArray();
        return errors.Length == 0 ? label + ": aucune"
            : $"{label} ({errors.Length}) médiane {Median(errors):F1} m";
    }

    internal static double Median(IReadOnlyList<double> sorted) =>
        sorted.Count == 0 ? double.NaN
        : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2]
        : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    internal static double Percentile90(IReadOnlyList<double> sorted) =>
        sorted.Count == 0 ? double.NaN
        : sorted[Math.Clamp((int)Math.Ceiling(sorted.Count * .9) - 1, 0, sorted.Count - 1)];

    private static double NormalizeYaw(double degrees)
    {
        var signed = ((degrees + 180) % 360 + 360) % 360 - 180;
        return signed;
    }
}
