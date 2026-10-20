using RaidPilot.Overlay;

namespace RaidPilot.Beacon;

internal readonly record struct BeaconCalibrationSegment(
    ScreenshotObservation? PreviousExact,
    RealtimePose? PredictedPoseAtCapture,
    long HipCounts,
    long AdsCounts,
    long HipMagnitude,
    long AdsMagnitude,
    double WalkPath,
    double SprintPath,
    double SlowPath,
    bool YawClean,
    bool SpeedClean);

internal sealed class BeaconAdaptiveCalibration
{
    private readonly string _path;
    private readonly RealtimeCalibrationSource _source;
    private readonly PrecisionCalibration _calibration = new();
    private readonly PrecisionResiduals _residuals = new();

    private ScreenshotObservation? _previousExact;
    private long _hipCounts;
    private long _adsCounts;
    private long _hipMagnitude;
    private long _adsMagnitude;
    private double _walkPath;
    private double _sprintPath;
    private double _slowPath;
    private bool _yawClean = true;
    private bool _speedClean = true;

    internal BeaconAdaptiveCalibration(
        string path,
        RealtimeCalibrationSource source)
    {
        _path = path;
        _source = source;
    }

    internal int LearnedUpdates { get; private set; }

    internal double? LastResidualMeters => _residuals.Last?.Meters;

    internal double? MedianResidualMeters => _residuals.MedianMeters;

    internal string LastDecision => _calibration.LastDecision;

    internal void ResetRaid()
    {
        _previousExact = null;
        _calibration.Reset();
        _residuals.Reset();
        ResetSegment();
    }

    internal void MarkTrackingGap()
    {
        _yawClean = false;
        _speedClean = false;
    }

    internal void RecordMouse(long counts, bool aiming, bool trackable)
    {
        if (!trackable)
        {
            if (counts != 0)
            {
                _yawClean = false;
            }
            return;
        }

        if (counts == 0)
        {
            return;
        }

        if (aiming)
        {
            _adsCounts += counts;
            _adsMagnitude += Math.Abs((long)counts);
        }
        else
        {
            _hipCounts += counts;
            _hipMagnitude += Math.Abs((long)counts);
        }
    }

    internal void RecordMotion(
        RealtimePose before,
        RealtimePose after,
        double forward,
        double right,
        bool sprint,
        bool slow,
        bool trackable,
        double elapsedSeconds)
    {
        if (!trackable
            || elapsedSeconds <= 0
            || elapsedSeconds >= 0.12)
        {
            MarkTrackingGap();
            return;
        }

        var dx = after.X - before.X;
        var dz = after.Z - before.Z;
        var distance = Math.Sqrt((dx * dx) + (dz * dz));
        if (!double.IsFinite(distance))
        {
            _speedClean = false;
            return;
        }

        var commandedMovement = Math.Abs(forward) > 0.01
            || Math.Abs(right) > 0.01;
        if (!commandedMovement)
        {
            // Coasting/braking cannot identify an individual gait speed.
            if (distance > 0.035)
            {
                _speedClean = false;
            }
            return;
        }

        // Persistent speed learning is intentionally restricted to clean,
        // almost-straight forward segments. Strafing/backpedalling use
        // separate multipliers and would otherwise bias the player's base speed.
        if (forward < 0.95 || Math.Abs(right) > 0.05)
        {
            _speedClean = false;
            return;
        }

        if (slow)
        {
            _slowPath += distance;
        }
        else if (sprint)
        {
            _sprintPath += distance;
        }
        else
        {
            _walkPath += distance;
        }
    }

    internal BeaconCalibrationSegment CaptureSegment(
        RealtimePose? predictedPoseAtCapture)
    {
        var snapshot = new BeaconCalibrationSegment(
            _previousExact,
            predictedPoseAtCapture,
            _hipCounts,
            _adsCounts,
            _hipMagnitude,
            _adsMagnitude,
            _walkPath,
            _sprintPath,
            _slowPath,
            _yawClean,
            _speedClean);

        ResetSegment();
        return snapshot;
    }

    internal bool ObserveExact(
        ScreenshotObservation observation,
        BeaconCalibrationSegment segment,
        RealtimePoseEstimator estimator)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(estimator);

        if (segment.PredictedPoseAtCapture is { } predicted)
        {
            _residuals.Observe(predicted, observation);
        }

        var previous = segment.PreviousExact;
        _previousExact = observation;
        if (previous is null)
        {
            return false;
        }

        var before = estimator.Settings;

        // Yaw and movement have independent cleanliness rules. Learn each
        // channel separately so a strafe does not throw away a useful pure
        // mouse rotation, and vice versa.
        var afterYaw = _calibration.Learn(
            before,
            previous,
            observation,
            predicted: null,
            segment.HipCounts,
            segment.AdsCounts,
            segment.HipMagnitude,
            segment.AdsMagnitude,
            walkPath: 0,
            sprintPath: 0,
            slowPath: 0,
            clean: segment.YawClean);

        var afterSpeed = _calibration.Learn(
            afterYaw,
            previous,
            observation,
            segment.PredictedPoseAtCapture,
            hipCounts: 0,
            adsCounts: 0,
            hipMagnitude: 0,
            adsMagnitude: 0,
            segment.WalkPath,
            segment.SprintPath,
            segment.SlowPath,
            clean: segment.SpeedClean);

        if (afterSpeed == before)
        {
            return false;
        }

        estimator.ApplySettings(afterSpeed);
        if (RealtimeCalibrationStore.TrySave(
            _path,
            afterSpeed,
            _source))
        {
            LearnedUpdates++;
        }
        return true;
    }

    private void ResetSegment()
    {
        _hipCounts = 0;
        _adsCounts = 0;
        _hipMagnitude = 0;
        _adsMagnitude = 0;
        _walkPath = 0;
        _sprintPath = 0;
        _slowPath = 0;
        _yawClean = true;
        _speedClean = true;
    }

    internal static string GetCalibrationPath(string memberId)
    {
        var safeId = new string(
            (memberId ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Take(64)
                .ToArray());
        if (string.IsNullOrWhiteSpace(safeId))
        {
            safeId = "default";
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaidPilot",
            "BeaconCalibration",
            safeId + ".json");
    }
}