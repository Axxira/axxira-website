using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>Bounded RAM-only rotation samples. No input injection, recording to disk or image capture.</summary>
internal sealed class PrecisionYawTimeline
{
    internal readonly record struct Sample(long At, int Counts, bool Aiming);
    private readonly Sample[] _ring;
    private int _start, _count;
    private long _discardedThrough = -1, _lastAt;
    internal PrecisionYawTimeline(int capacity = 4096)
    {
        if (capacity < 2 || capacity > 16384) throw new ArgumentOutOfRangeException(nameof(capacity));
        _ring = new Sample[capacity];
    }
    internal int Count => _count;
    internal void Reset() { Array.Clear(_ring); _start = _count = 0; _discardedThrough = -1; _lastAt = 0; }
    internal void Record(long at, int counts, bool aiming)
    {
        if (at < 0) throw new ArgumentOutOfRangeException(nameof(at));
        if (counts == 0) return;
        at = Math.Max(at, _lastAt); _lastAt = at;
        if (_count == _ring.Length)
        {
            _discardedThrough = Math.Max(_discardedThrough, _ring[_start].At);
            _start = (_start + 1) % _ring.Length; _count--;
        }
        _ring[(_start + _count++) % _ring.Length] = new(at, counts, aiming);
    }
    internal bool CopyInterval(long start, long end, List<Sample> target)
    {
        target.Clear();
        if (start < 0 || end < start || start < _discardedThrough) return false;
        for (int i = 0; i < _count; i++)
        {
            var sample = _ring[(_start + i) % _ring.Length];
            if (sample.At > end) break;
            if (sample.At > start) target.Add(sample);
        }
        return true;
    }
}

internal readonly record struct PrecisionMotionResult(RealtimePose Pose, double PathMeters, int Slices, double TotalYawDegrees);

/// <summary>
/// Replays movement boundaries and rotation in slices no longer than 5 ms of MODEL time.
/// This is integration granularity, NOT a 200 Hz screen-capture or UI timer.
/// The original estimator, posture/speed controls and conservative image evidence are retained.
/// </summary>
internal sealed class PrecisionMotionIntegrator
{
    internal const double MaximumStepSeconds = 0.005;
    internal const int MaximumSlices = 512;
    private readonly long _frequency;
    private readonly List<long> _boundaries = new(MaximumSlices + 1);
    private readonly List<PrecisionYawTimeline.Sample> _yaw = new(4096);
    private readonly List<Step> _steps = new(MaximumSlices);
    private readonly record struct Step(long End, double Seconds, double Forward, double Right, double Hip, double Ads);
    internal string LastFallback { get; private set; } = "";
    internal static double MovementActivity(double forwardHeld, double backwardHeld, double leftHeld, double rightHeld) =>
        Math.Clamp(Math.Max(Math.Max(forwardHeld, backwardHeld), Math.Max(leftHeld, rightHeld)), 0, 1);
    internal PrecisionMotionIntegrator(long frequency = 0) { _frequency = frequency > 0 ? frequency : Stopwatch.Frequency; }

    internal bool TryAdvance(RealtimePoseEstimator estimator, RawKeyboardTimeline keyboard, PrecisionYawTimeline mouse,
        long start, long end, Keys forwardKey, Keys backwardKey, Keys leftKey, Keys rightKey,
        bool sprint, bool slow, double speedScale, RealtimeMotionEvidence evidence, int expectedMouseCounts,
        out PrecisionMotionResult result)
    {
        result = default; LastFallback = ""; _boundaries.Clear(); _steps.Clear();
        var duration = (end - start) / (double)_frequency;
        if (!estimator.IsAnchored || start < 0 || end <= start || duration > 0.75)
            return Fail("intervalle hors budget");
        if (!mouse.CopyInterval(start, end, _yaw)) return Fail("historique de rotation incomplet");
        long sum = 0; foreach (var sample in _yaw) sum += sample.Counts;
        if (sum != expectedMouseCounts) return Fail("rotation non entièrement horodatée");
        Span<Keys> keys = stackalloc Keys[4] { forwardKey, backwardKey, leftKey, rightKey };
        if (!keyboard.CopyTransitionTimes(start, end, keys, _boundaries)) return Fail("historique clavier incomplet");
        // Short-lived turns and key changes need finer integration than steady straight walking.
        // This adapts computation only; it does not increase the raw input or capture frequency.
        var stepSeconds = _boundaries.Count > 0 || _yaw.Count > 0 ? 0.001 : MaximumStepSeconds;
        long sliceTicks = Math.Max(1, (long)Math.Round(stepSeconds * _frequency));
        for (long at = start + sliceTicks; at < end; at += sliceTicks) _boundaries.Add(at);
        _boundaries.Add(end); _boundaries.Sort();
        long cursor = start; int mouseIndex = 0;
        foreach (var boundary in _boundaries)
        {
            if (boundary <= cursor) continue;
            if (_steps.Count >= MaximumSlices) return Fail("budget de sous-pas dépassé");
            if (!keyboard.TrySample(forwardKey, cursor, boundary, out var f)
                || !keyboard.TrySample(backwardKey, cursor, boundary, out var b)
                || !keyboard.TrySample(leftKey, cursor, boundary, out var l)
                || !keyboard.TrySample(rightKey, cursor, boundary, out var r)) return Fail("transition manquante");
            double hip = 0, ads = 0;
            while (mouseIndex < _yaw.Count && _yaw[mouseIndex].At <= boundary)
            {
                var sample = _yaw[mouseIndex++];
                if (sample.Aiming) ads += sample.Counts; else hip += sample.Counts;
            }
            _steps.Add(new(boundary, (boundary - cursor) / (double)_frequency,
                f.HeldFraction - b.HeldFraction, r.HeldFraction - l.HeldFraction, hip, ads));
            cursor = boundary;
        }
        // All inputs were validated before touching the estimator: a fallback cannot integrate twice.
        var pose = estimator.CurrentPose!.Value; double path = 0, yaw = 0;
        foreach (var step in _steps)
        {
            var angle = step.Hip * estimator.Settings.HipYawDegreesPerMouseCount
                + step.Ads * estimator.Settings.AdsYawDegreesPerMouseCount;
            bool useAds = estimator.Settings.HipYawDegreesPerMouseCount == 0;
            double sensitivity = useAds ? estimator.Settings.AdsYawDegreesPerMouseCount : estimator.Settings.HipYawDegreesPerMouseCount;
            var next = estimator.Advance(step.Seconds, step.Forward, step.Right, sprint && step.Forward > 0,
                slow, sensitivity == 0 ? 0 : angle / sensitivity, useAds, speedScale, evidence);
            path += Math.Sqrt(Math.Pow(next.X - pose.X, 2) + Math.Pow(next.Z - pose.Z, 2));
            yaw += angle; pose = next;
        }
        result = new(pose, path, _steps.Count, yaw); return true;
    }
    private bool Fail(string reason) { LastFallback = reason; return false; }
}
