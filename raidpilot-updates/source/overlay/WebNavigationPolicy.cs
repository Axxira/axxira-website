namespace RaidPilot.Overlay;

/// <summary>
/// Pure navigation-state decisions kept outside WebView2 event handlers so a
/// canceled or stale completion can never make the current map look failed.
/// </summary>
internal static class WebNavigationPolicy
{
    internal const string OfflineMapHost = "raidpilot.local";
    internal const string OfflineMapPath = "/OfflineMap.html";

    internal static bool TryGetAllowedMapUri(string? value, out Uri? uri)
    {
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
                OfflineMapHost,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                candidate.AbsolutePath,
                OfflineMapPath,
                StringComparison.OrdinalIgnoreCase)
            && IsSafeOfflineMapQuery(candidate.Query);

        if (!remoteMap && !offlineMap)
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static bool IsSafeOfflineMapQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        foreach (var part in query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..separator]);
            if (!string.Equals(key, "map", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(part[(separator + 1)..]);
            return value.Length is > 0 and <= 64
                && value.All(character =>
                    char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_');
        }

        return false;
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
