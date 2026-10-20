namespace RaidPilot.Overlay;

/// <summary>Small robust windows; one anomalous or curved segment cannot rewrite learned speeds.</summary>
internal sealed class PrecisionCalibration
{
    private readonly Dictionary<string, Queue<double>> _windows = new(StringComparer.Ordinal);
    internal int Accepted { get; private set; }
    internal int Rejected { get; private set; }
    internal string LastDecision { get; private set; } = "en attente de segments vérifiables";
    internal void Reset() { _windows.Clear(); Accepted = Rejected = 0; LastDecision = "nouvelle session"; }

    internal double? Propose(string channel, double candidate, double current, double minimum, double maximum)
    {
        if (!double.IsFinite(candidate) || !double.IsFinite(current) || current <= 0 || candidate < minimum
            || candidate > maximum || candidate / current is < 0.6 or > 1.6)
        { Reject("valeur hors garde-fous"); return null; }
        if (!_windows.TryGetValue(channel, out var samples)) _windows[channel] = samples = new Queue<double>(7);
        // Reject an isolated gross deviation before it enters an already stable window.
        if (samples.Count >= 3)
        {
            var median = Median(samples);
            var mad = Median(samples.Select(x => Math.Abs(x - median)));
            if (Math.Abs(candidate - median) > Math.Max(0.12 * median, 4.5 * mad))
            { Reject("valeur isolée incompatible avec les précédentes"); return null; }
        }
        if (samples.Count == 7) samples.Dequeue(); samples.Enqueue(candidate); Accepted++;
        if (samples.Count < 3) { LastDecision = "confirmation de calibration " + samples.Count + "/3"; return null; }
        var center = Median(samples);
        if (Median(samples.Select(x => Math.Abs(x - center))) > center * 0.08)
        { Reject("échantillons trop dispersés"); return null; }
        // At most 3% per accepted segment; no large sudden retuning in the middle of a raid.
        var correction = Math.Clamp(1 + (center / current - 1) * 0.25, 0.97, 1.03);
        LastDecision = "calibration robuste : " + channel;
        return Math.Clamp(current * correction, minimum, maximum);
    }

    internal static bool IsSpeedSegmentEligible(double predictedDistance, double actualDistance,
        double pathDistance, double alignment, double verticalChange, bool clean)
    {
        return clean && new[] { predictedDistance, actualDistance, pathDistance, alignment, verticalChange }.All(double.IsFinite)
            && predictedDistance >= 10 && actualDistance >= 6 && pathDistance >= 10
            && predictedDistance / pathDistance >= 0.97 && predictedDistance / pathDistance <= 1.02
            && alignment >= 0.995 && Math.Abs(verticalChange) <= 1.5
            && actualDistance / predictedDistance is >= 0.65 and <= 1.35;
    }

    internal RealtimePoseEstimatorSettings Learn(RealtimePoseEstimatorSettings settings,
        ScreenshotObservation previous, ScreenshotObservation observed, RealtimePose? predicted,
        long hipCounts, long adsCounts, long hipMagnitude, long adsMagnitude,
        double walkPath, double sprintPath, double slowPath, bool clean)
    {
        var result = settings;
        var observedYaw = SignedAngle(observed.YawDegrees - previous.YawDegrees);
        // Native capture-to-capture only. Near-full turns and heavily cancelling mouse motion
        // cannot resolve a unique sensitivity from endpoints. Mixed ADS/hip is not one variable.
        var totalMagnitude = (double)hipMagnitude + adsMagnitude;
        if (clean && totalMagnitude >= 80 && Math.Abs(observedYaw) is >= 8 and <= 150)
        {
            var pureHip = hipMagnitude >= totalMagnitude * 0.99 && hipCounts != 0
                && Math.Abs((double)hipCounts) >= hipMagnitude * 0.8;
            var pureAds = adsMagnitude >= totalMagnitude * 0.99 && adsCounts != 0
                && Math.Abs((double)adsCounts) >= adsMagnitude * 0.8;
            if (pureHip && Propose("orientation normale", observedYaw / hipCounts,
                settings.HipYawDegreesPerMouseCount, 0.005, 0.5) is { } hip)
                result = result with { HipYawDegreesPerMouseCount = hip };
            if (pureAds && Propose("orientation visée", observedYaw / adsCounts,
                settings.AdsYawDegreesPerMouseCount, 0.005, 0.5) is { } ads)
                result = result with { AdsYawDegreesPerMouseCount = ads };
        }
        if (predicted is not { } p) return result;
        double px = p.X - previous.X, pz = p.Z - previous.Z;
        double ax = observed.X - previous.X, az = observed.Z - previous.Z;
        double pd = Math.Sqrt(px * px + pz * pz), ad = Math.Sqrt(ax * ax + az * az);
        var path = walkPath + sprintPath + slowPath;
        var alignment = pd > 0 && ad > 0 ? (px * ax + pz * az) / (pd * ad) : 0;
        if (!IsSpeedSegmentEligible(pd, ad, path, alignment, observed.Y - previous.Y, clean))
        { Reject("segment court, courbe, vertical ou non observable"); return result; }
        var ratio = ad / pd;
        // A mixed gait does not tell us which individual speed was wrong.
        if (walkPath >= path * 0.98 && Propose("marche", settings.WalkSpeedMetersPerSecond * ratio,
            settings.WalkSpeedMetersPerSecond, 1.2, 3.5) is { } walk)
            return result with { WalkSpeedMetersPerSecond = walk };
        if (sprintPath >= path * 0.98 && Propose("course", settings.SprintSpeedMetersPerSecond * ratio,
            settings.SprintSpeedMetersPerSecond, 3.5, 8) is { } sprint)
            return result with { SprintSpeedMetersPerSecond = sprint };
        if (slowPath >= path * 0.98 && Propose("marche lente", settings.SlowSpeedMetersPerSecond * ratio,
            settings.SlowSpeedMetersPerSecond, 0.4, 1.8) is { } slow)
            return result with { SlowSpeedMetersPerSecond = slow };
        return result;
    }

    private void Reject(string reason) { Rejected++; LastDecision = reason; }
    private static double Median(IEnumerable<double> values)
    {
        var array = values.Order().ToArray();
        return array.Length % 2 == 1 ? array[array.Length / 2] : (array[array.Length / 2 - 1] + array[array.Length / 2]) / 2;
    }
    internal static double SignedAngle(double degrees) { var d = ((degrees + 180) % 360 + 360) % 360 - 180; return d; }
}

/// <summary>Residuals measured BEFORE a native correction. These are not continuous accuracy guarantees.</summary>
internal sealed class PrecisionResiduals
{
    internal readonly record struct Error(double Meters, double YawDegrees);
    private readonly Queue<Error> _errors = new(32);
    internal int Count => _errors.Count;
    internal Error? Last => _errors.Count > 0 ? _errors.Last() : null;
    internal double? MedianMeters => _errors.Count == 0 ? null : _errors.Select(e => e.Meters).Order().ElementAt(_errors.Count / 2);
    internal void Reset() => _errors.Clear();
    internal void Observe(RealtimePose prediction, ScreenshotObservation measurement)
    {
        if (!new[] { prediction.X, prediction.Z, prediction.YawDegrees, measurement.X, measurement.Z, measurement.YawDegrees }.All(double.IsFinite)) return;
        var distance = Math.Sqrt(Math.Pow(measurement.X - prediction.X, 2) + Math.Pow(measurement.Z - prediction.Z, 2));
        if (_errors.Count == 32) _errors.Dequeue();
        _errors.Enqueue(new(distance, Math.Abs(PrecisionCalibration.SignedAngle(measurement.YawDegrees - prediction.YawDegrees))));
    }
}
