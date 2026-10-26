using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Privacy-minimizing diagnostic to compare visual navigation sessions.
/// Never serializes game coordinates, keyframes, icon pixels, or player IDs.
/// </summary>
internal static class CortexV4Report
{
    internal static string Build(
        IReadOnlyList<PrecisionDriftSample> nativeF8Residuals,
        CortexNavigationEngine navigation,
        CortexVisualIntelligence vision,
        CortexSpatialReliabilityAtlas atlas,
        bool v4Enabled)
    {
        ArgumentNullException.ThrowIfNull(nativeF8Residuals);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(atlas);

        var error = nativeF8Residuals
            .Where(s => double.IsFinite(s.ErrorMeters)
                && s.ErrorMeters is >= 0 and <= 200
                && double.IsFinite(s.Seconds)
                && s.Seconds is >= 4 and <= 900)
            .Select(s => s.ErrorMeters).Order().ToArray();

        static double? Mean(double[] values) =>
            values.Length == 0 ? null : values.Average();
        static double? Percentile90(double[] values) =>
            values.Length == 0 ? null
                : values[Math.Clamp((int)Math.Ceiling(0.90 * values.Length) - 1,
                    0, values.Length - 1)];

        var risk = navigation.DriftRisk;
        var report = new
        {
            schemaVersion = 1,
            engine = "CORTEX V4",
            experimental = true,
            enabled = v4Enabled,
            nativeF8 = new
            {
                comparable = navigation.ComparableF8Anchors,
                measuredIntervals = navigation.MeasuredNativeF8Intervals,
                averageIntervalSeconds = double.IsFinite(
                    navigation.AverageNativeF8IntervalSeconds)
                    ? (double?)navigation.AverageNativeF8IntervalSeconds : null,
                under6Seconds = navigation.ShortNativeF8Intervals,
                from6To10Seconds = navigation.MediumNativeF8Intervals,
                over10Seconds = navigation.LongNativeF8Intervals,
                measuredMeanErrorMeters = Mean(error),
                measuredP90ErrorMeters = Percentile90(error),
                suspicious = navigation.SuspiciousF8Anchors
            },
            visual = new
            {
                accepted = vision.Accepted,
                rejected = vision.Rejected,
                validatedLoopClosures = vision.VerifiedLoopClosures,
                mode = vision.Mode.ToString()
            },
            spatial = new
            {
                localCoarseRiskSectors = atlas.Count,
                verifiedF8RiskObservations = atlas.Observations
            },
            learning = new
            {
                calibrated = risk.Calibrated,
                samples = risk.SampleCount,
                conservativeF8CeilingSeconds = risk.MaxIntervalSeconds,
                estimatedErrorAt8Meters = risk.Calibrated
                    ? (double?)risk.EstimatedErrorAt8Meters : null,
                estimatedErrorAt12Meters = risk.Calibrated
                    ? (double?)risk.EstimatedErrorAt12Meters : null
            },
            limitations = "Risk model is empirical; true position error " +
                "requires independent native F8 measurements. No claim of " +
                "accuracy improvement without matched in-game comparison."
        };
        return JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true });
    }
}
