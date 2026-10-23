using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>
/// Export only bounded F8 residual statistics. No screenshots, world coordinates,
/// keys, member identifiers, map names or network information are emitted.
/// </summary>
internal static class PrecisionV4Report
{
    internal static string Build(
        IReadOnlyList<PrecisionDriftSample> samples,
        int ignoredIntervals,
        long visualAccepted,
        long visualRejected)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var bounded = samples.TakeLast(32).Select(s => new
        {
            mode = s.PrecisionV4Enabled ? "V4" : s.PrecisionV2Enabled ? "V2" : "V1",
            seconds = Math.Round(s.Seconds, 2),
            errorMeters = Math.Round(s.ErrorMeters, 3),
            headingErrorDegrees = Math.Round(s.HeadingErrorDegrees, 2),
            modelUncertaintyMeters = Math.Round(s.ModelUncertaintyMeters, 3),
        }).ToArray();
        var report = new
        {
            schemaVersion = 1,
            kind = "RaidPilot Precision F8 residual audit",
            generatedUtc = DateTimeOffset.UtcNow,
            actualEftF8Pairs = bounded.Length,
            ignoredIntervals = Math.Max(0, ignoredIntervals),
            visualCorrectionsAccepted = Math.Max(0L, visualAccepted),
            visualCandidatesRejected = Math.Max(0L, visualRejected),
            explanation = "Pairs of native F8 anchors only. Not continuous ground truth. "
                + "The reported uncertainty is a model estimate, not a GPS guarantee. "
                + "Compare similar maps, input profiles and time spans.",
            samples = bounded,
        };
        return JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true });
    }
}
