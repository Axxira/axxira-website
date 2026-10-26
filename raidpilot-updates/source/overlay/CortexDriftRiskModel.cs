namespace RaidPilot.Overlay;

/// <summary>
/// Bounded online risk calibration from actual, timestamp-verified native F8
/// residuals. Not a position estimator, neural network or accuracy guarantee.
/// Used only to LIMIT the already proven 4/8/12-second adaptive policy.
///
/// Learns inside one map/raid/session: no coordinates, images or paths saved.
/// Samples are deliberately not persisted or shared with other players.
/// </summary>
internal sealed class CortexDriftRiskModel
{
    internal const int MinimumSamples = 6;
    internal const int Capacity = 24;

    private readonly Queue<Residual> _recent = new(Capacity);
    private CortexDriftRiskSnapshot _current = ColdStart;

    private static CortexDriftRiskSnapshot ColdStart =>
        new(0, 12, double.NaN, double.NaN, double.NaN, double.NaN,
            "Apprentissage en cours; règles visuelles habituelles");

    internal CortexDriftRiskSnapshot Current => _current;

    internal void Reset()
    {
        _recent.Clear();
        _current = ColdStart;
    }

    /// <summary>
    /// The caller must already have established that the F8 was native,
    /// timestamp-comparable, and belongs to the current raid/session.
    /// Time intervals over 30 seconds are not extrapolated as normal cadence.
    /// </summary>
    internal bool Observe(PrecisionDriftSample sample)
    {
        if (!double.IsFinite(sample.ErrorMeters)
            || !double.IsFinite(sample.HeadingErrorDegrees)
            || !double.IsFinite(sample.ModelUncertaintyMeters)
            || !double.IsFinite(sample.Seconds)
            || sample.ErrorMeters is < 0 or > 200
            || sample.HeadingErrorDegrees is < 0 or > 180
            || sample.ModelUncertaintyMeters is < 0 or > 200
            || sample.Seconds is < 4 or > 30)
            return false;

        var squareRootTime = Math.Sqrt(sample.Seconds);
        var observation = new Residual(
            sample.ErrorMeters / squareRootTime,
            sample.HeadingErrorDegrees / squareRootTime);

        if (_recent.Count == Capacity) _recent.Dequeue();
        _recent.Enqueue(observation);
        Update();
        return true;
    }

    private void Update()
    {
        var n = _recent.Count;
        if (n < MinimumSamples)
        {
            _current = ColdStart with { SampleCount = n };
            return;
        }

        // Nearest-rank p90 is deliberately conservative for small samples.
        // These are empirical envelopes, NOT calibrated probabilities or
        // distribution-free prediction guarantees for a moving player.
        var errorRate = UpperQuantile(_recent.Select(x => x.MetresPerRootSecond));
        var yawRate = UpperQuantile(_recent.Select(x => x.DegreesPerRootSecond));
        var at8 = errorRate * Math.Sqrt(8.0) + 0.20;
        var at12 = errorRate * Math.Sqrt(12.0) + 0.20;
        var yaw8 = yawRate * Math.Sqrt(8.0) + 1.0;
        var yaw12 = yawRate * Math.Sqrt(12.0) + 1.0;

        var ceiling = 12;
        var reason = "Historique F8 stable; limites habituelles préservées";
        if (at8 >= 2.25 || yaw8 >= 15.0)
        {
            ceiling = 4;
            reason = "Dérive mesurée trop élevée pour espacer les F8";
        }
        else if (at12 >= 1.25 || yaw12 >= 12.0)
        {
            ceiling = 8;
            reason = "Risque mesuré : intervalle limité à 8 secondes";
        }

        // Abrupt nonstationary drift can invalidate a calm historical average.
        // Do not wait for the rolling 24-sample window to fill up.
        if (n >= 8)
        {
            var rates = _recent.Select(x => x.MetresPerRootSecond).ToArray();
            var previous = rates.Skip(n - 8).Take(4).Average();
            var latest = rates.Skip(n - 4).Average();
            if (latest > Math.Max(0.10, previous) * 2.5
                && latest * Math.Sqrt(8.0) + 0.20 >= 1.5)
            {
                ceiling = 4;
                reason = "Dérive en hausse rapide sur les derniers F8";
            }
        }

        _current = new(n, ceiling, at8, at12, yaw8, yaw12, reason);
    }

    private static double UpperQuantile(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var index = Math.Clamp((int)Math.Ceiling(sorted.Length * 0.9) - 1,
            0, sorted.Length - 1);
        return sorted[index];
    }

    private readonly record struct Residual(
        double MetresPerRootSecond, double DegreesPerRootSecond);
}

internal readonly record struct CortexDriftRiskSnapshot(
    int SampleCount,
    int MaxIntervalSeconds,
    double EstimatedErrorAt8Meters,
    double EstimatedErrorAt12Meters,
    double EstimatedYawAt8Degrees,
    double EstimatedYawAt12Degrees,
    string Reason)
{
    internal bool Calibrated => SampleCount >= CortexDriftRiskModel.MinimumSamples;
}
