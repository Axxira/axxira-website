namespace RaidPilot.Overlay;

/// <summary>
/// Pure navigation-state decisions kept outside WebView2 event handlers so a
/// canceled or stale completion can never make the current map look failed.
/// </summary>
internal static class WebNavigationPolicy
{
    internal static bool TryGetAllowedMapUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttps
            || !string.Equals(candidate.Host, "tarkov.dev", StringComparison.OrdinalIgnoreCase)
            || candidate.Port != 443
            || !candidate.AbsolutePath.StartsWith("/map/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    internal static bool IsActiveCompletion(
        ulong navigationId,
        ulong? activeNavigationId,
        bool navigationStartPending)
    {
        return !navigationStartPending
            && activeNavigationId.HasValue
            && activeNavigationId.Value == navigationId;
    }

    internal static bool CanContinueAfterAwait(
        long expectedSequence,
        long currentSequence,
        bool navigationInProgress)
    {
        return !navigationInProgress && expectedSequence == currentSequence;
    }
}
