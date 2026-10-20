namespace RaidPilot.Overlay;

/// <summary>Classifies already-read text only; never sends input or acquires another image.</summary>
internal readonly record struct RaidPanelReadPlan(bool ReadTimer, bool ReadQuests, bool UpdateExtractions)
{
    internal static RaidPanelReadPlan FromLines(IEnumerable<string> lines, bool extractionsComplete)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var hasPanel = ExtractionAdvisor.SelectUniqueOcrExfilLines(lines).Count > 0;
        // A complete EXFIL list does not imply a successful clock read. Every later
        // user-supplied O panel remains eligible for synchronization, independently
        // of catalog availability and without redoing already-confirmed extractions.
        return new(hasPanel, !hasPanel, hasPanel && !extractionsComplete);
    }
}

internal enum RaidTimerReadState
{
    Waiting,
    Reading,
    Unreadable,
    ConfirmationRequired,
    Synchronized,
    Failed,
}
