using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>
/// Relie une capture créée après l'injection F8 à la requête exacte qui l'a
/// déclenchée. Les fichiers déjà présents, trop anciens ou d'un autre raid ne
/// peuvent jamais être pris pour une capture automatique.
/// </summary>
internal sealed class AutomaticReanchorRequest
{
    private static readonly TimeSpan FileClockTolerance = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumCaptureDelay = TimeSpan.FromSeconds(2);
    private const double DeliveryWindowSeconds = 4.0;

    internal AutomaticReanchorRequest(
        long requestedAtTimestamp,
        DateTimeOffset requestedAtUtc,
        int raidGeneration,
        IReadOnlySet<string> existingFileNames)
    {
        RequestedAtTimestamp = requestedAtTimestamp;
        RequestedAtUtc = requestedAtUtc;
        RaidGeneration = raidGeneration;
        ExistingFileNames = existingFileNames;
    }

    internal long RequestedAtTimestamp { get; }

    internal DateTimeOffset RequestedAtUtc { get; }

    internal int RaidGeneration { get; }

    internal IReadOnlySet<string> ExistingFileNames { get; }

    internal string? CandidateFileName { get; private set; }

    internal bool IsAmbiguous { get; private set; }

    internal bool MovementOccurred { get; set; }

    internal bool CleanupScheduled { get; set; }

    internal bool IsWithinWindow(long nowTimestamp, double seconds)
    {
        return nowTimestamp >= RequestedAtTimestamp
            && (nowTimestamp - RequestedAtTimestamp) / (double)Stopwatch.Frequency <= seconds;
    }

    internal bool TryRegisterCandidate(
        string fileName,
        DateTimeOffset fileTimestampUtc,
        int raidGeneration,
        long nowTimestamp)
    {
        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal)
            || raidGeneration != RaidGeneration
            // FileSystemWatcher can deliver the notification late.  Give it a
            // wider delivery window, while the file's own timestamp below
            // still proves that the screenshot was created just after F8.
            || !IsWithinWindow(nowTimestamp, DeliveryWindowSeconds)
            || ExistingFileNames.Contains(fileName)
            || fileTimestampUtc < RequestedAtUtc - FileClockTolerance
            || fileTimestampUtc > RequestedAtUtc + MaximumCaptureDelay)
        {
            return false;
        }

        if (CandidateFileName is null)
        {
            CandidateFileName = fileName;
        }
        else if (!string.Equals(CandidateFileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            IsAmbiguous = true;
        }

        return true;
    }
}