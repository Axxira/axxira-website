using Microsoft.Web.WebView2.Core;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private const string OfflineMapHost = "raidpilot.local";
    private bool _offlineMapMode;

    private static readonly HttpClient OfflineProbeClient = new()
    {
        Timeout = TimeSpan.FromSeconds(4),
    };

    private static Uri BuildOfflineMapUri(string mapName) =>
        new(
            $"https://{OfflineMapHost}/OfflineMap.html?map="
            + Uri.EscapeDataString(
                CollectiveAtlasProtocol.NormalizeMapId(mapName)));

    private static async Task<bool> ShouldPreferOfflineMapAsync(
        Uri remoteMapUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                remoteMapUri);
            request.Headers.UserAgent.ParseAdd(
                "RaidPilot-Overlay/0.14");
            using var response = await OfflineProbeClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return !response.IsSuccessStatusCode;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsOfflineMapUri(Uri? uri) =>
        uri is not null
        && string.Equals(
            uri.Host,
            OfflineMapHost,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            uri.AbsolutePath,
            "/OfflineMap.html",
            StringComparison.OrdinalIgnoreCase);

    private static string? GetOfflineMapName(Uri? uri)
    {
        if (RaidPilotLocalMapHost.IsLocalMapUri(uri) && RaidPilotLocalMapHost.TryExtractMapKey(uri?.AbsoluteUri, out var localFirstMap)) return localFirstMap;

        if (!IsOfflineMapUri(uri))
        {
            return null;
        }

        var query = uri!.Query.TrimStart('?');
        foreach (var segment in query.Split(
            '&',
            StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2
                && string.Equals(
                    pair[0],
                    "map",
                    StringComparison.OrdinalIgnoreCase))
            {
                return CollectiveAtlasProtocol.NormalizeMapId(
                    Uri.UnescapeDataString(pair[1]));
            }
        }
        return null;
    }

    private bool IsCurrentSourceForMap(
        Uri? source,
        EftMapRoute map)
    {
        if (source is null)
        {
            return false;
        }

        if (string.Equals(
                source.Host,
                map.Url.Host,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                source.AbsolutePath,
                map.Url.AbsolutePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var offlineMap = GetOfflineMapName(source);
        return offlineMap is not null
            && string.Equals(
                offlineMap,
                map.NormalizedName,
                StringComparison.OrdinalIgnoreCase);
    }

    private void NavigateToOfflineMap(
        EftMapRoute map,
        string reason)
    {
        var core = _webView.CoreWebView2;
        if (core is null)
        {
            return;
        }

        _offlineMapMode = true;
        _pendingDetectedMap = null;
        SetStatus(
            $"Carte locale · {map.NormalizedName} · {reason}",
            Color.FromArgb(217, 170, 78));
        BeginWebNavigation(
            core,
            BuildOfflineMapUri(map.NormalizedName).AbsoluteUri);
    }

    private async Task<bool> TryFallbackToOfflineMapAsync(
        CoreWebView2NavigationCompletedEventArgs args)
    {
        var source = _webView.Source;
        if (IsOfflineMapUri(source)
            || source is null
            || !string.Equals(
                source.Host,
                "tarkov.dev",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var blocked = !args.IsSuccess;
        if (!blocked && _webView.CoreWebView2 is { } core)
        {
            try
            {
                var result = await core.ExecuteScriptAsync(
                    """
                    (() => {
                        const text = String(document.body?.innerText || '').toLowerCase();
                        const title = String(document.title || '').toLowerCase();
                        return text.includes('sorry, you have been blocked')
                            || text.includes('unable to access tarkov.dev')
                            || title.includes('you have been blocked');
                    })()
                    """);
                blocked = string.Equals(
                    result?.Trim(),
                    "true",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
            }
        }

        if (!blocked)
        {
            return false;
        }

        var map = _pendingDetectedMap;
        if (map is null
            && EftMapLogParser.TryNormalizeMapToken(
                _currentMap,
                out var normalized)
            && normalized is not null)
        {
            map = normalized;
        }

        if (map is null
            && EftMapLogParser.TryNormalizeMapToken(
                "customs",
                out var fallback)
            && fallback is not null)
        {
            map = fallback;
        }

        if (map is null)
        {
            return false;
        }

        _activeNavigationId = null;
        _activeNavigationUri = null;
        _navigationInProgress = false;
        _navigationStartPending = false;
        _webReady = false;

        NavigateToOfflineMap(
            map,
            args.IsSuccess
                ? "tarkov.dev bloqué"
                : "réseau indisponible");
        return true;
    }
}
