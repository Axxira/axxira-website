namespace RaidPilot.Overlay;

/// <summary>
/// Additional V4-only safety net for incremental learned speed/yaw updates.
/// Never persists a candidate after a single unexplained large F8 residual.
/// Uses only bounded error statistics; no player position is recorded.
/// </summary>
internal static class PrecisionV4CalibrationSafety
{
    internal static bool MayLearn(IReadOnlyList<PrecisionDriftSample> measurements)
    {
        if (measurements.Count < 4)
        {
            return true; // Base calibration already needs multiple clean segments.
        }
        var recent = measurements.TakeLast(4).ToArray();
        if (recent.Any(s => !double.IsFinite(s.ErrorMeters)
            || !double.IsFinite(s.ModelUncertaintyMeters)
            || s.ErrorMeters < 0 || s.ModelUncertaintyMeters < 0))
        {
            return false;
        }
        var last = recent[^1];
        var previousNormalized = recent.Take(3)
            .Select(s => s.ErrorMeters / Math.Max(0.5, s.ModelUncertaintyMeters))
            .Order().ToArray();
        var currentNormalized = last.ErrorMeters
            / Math.Max(0.5, last.ModelUncertaintyMeters);
        // A spike may mean collision, mistaken posture or an invalid visual
        // correction, not a new player running-speed calibration.
        return last.ErrorMeters <= Math.Max(6.0, last.ModelUncertaintyMeters * 2.5)
            || currentNormalized <= Math.Max(3.0,
                PrecisionDriftAudit.Median(previousNormalized) * 2.0);
    }
}
