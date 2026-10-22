namespace RaidPilot.Overlay;

/// <summary>
/// Pure navigation-state decisions kept outside WebView2 event handlers so a
/// canceled or stale completion can never make the current map look failed.
/// </summary>
internal static class WebNavigationPolicy
{
    internal static bool TryGetAllowedMapUri(string? value, out Uri? uri)
    {
        // LocalFirst uses its own host to preserve the legacy offline mapping.
        if (Uri.TryCreate(value, UriKind.Absolute, out var localFirstUri)
            && localFirstUri.Scheme == Uri.UriSchemeHttps && localFirstUri.Port == 443
            && string.Equals(localFirstUri.Host, "raidpilot-map.local", StringComparison.OrdinalIgnoreCase)
            && string.Equals(localFirstUri.AbsolutePath, "/index.html", StringComparison.OrdinalIgnoreCase))
        {
            uri = localFirstUri;
            return true;
        }

        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttps
            || candidate.Port != 443)
        {
            return false;
        }

        var remoteMap =
            string.Equals(
                candidate.Host,
                "tarkov.dev",
                StringComparison.OrdinalIgnoreCase)
            && candidate.AbsolutePath.StartsWith(
                "/map/",
                StringComparison.OrdinalIgnoreCase);

        var offlineMap =
            string.Equals(
                candidate.Host,
                "raidpilot.local",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                candidate.AbsolutePath,
                "/OfflineMap.html",
                StringComparison.OrdinalIgnoreCase);

        if (!remoteMap && !offlineMap)
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
