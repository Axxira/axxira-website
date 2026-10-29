using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace RaidPilot.Overlay;

/// <summary>
/// On-demand read-only bridge to tarkov.dev's supported JSON API. Only the
/// local RaidPilot renderer can request it, never arbitrary third-party pages.
/// All game data stays local (bounded AppData cache), with no paid service,
/// telemetry or player inventory upload.
/// </summary>
internal sealed partial class OverlayForm
{
    private static readonly HttpClient HideoutIntelClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };
    private readonly TarkovDevJsonCache _hideoutIntelCache = new();
    private readonly Dictionary<string, string> _hideoutIntelReady =
        new(StringComparer.Ordinal);
    private bool _hideoutIntelLoading;
    private const long HideoutIntelMaximumBytes = 5L * 1024L * 1024L;

    private static bool TryParseHideoutIntelRequest(
        string? origin, string? raw, out string mode)
    {
        mode = "";
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var url)
            || !RaidPilotLocalMapHost.IsLocalMapUri(url)
            || raw is null || raw.Length > 256)
            return false;

        try
        {
            using var parsed = JsonDocument.Parse(raw);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.GetString() != "raidpilot-hideout-intel-request"
                || !root.TryGetProperty("mode", out var gameMode)
                || gameMode.ValueKind != JsonValueKind.String)
                return false;
            mode = gameMode.GetString() ?? "";
            return mode is "regular" or "pve";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void OnHideoutIntelMessage(
        object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_formLifetime.IsCancellationRequested || IsDisposed
            || !_webReady || _navigationInProgress
            || !TryParseHideoutIntelRequest(args.Source, args.WebMessageAsJson,
                out var mode)
            || _webView.Source is not { } current
            || !Uri.TryCreate(args.Source, UriKind.Absolute, out var source)
            || Uri.Compare(source, current,
                UriComponents.SchemeAndServer | UriComponents.Path,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) != 0)
            return;
        _ = DeliverHideoutIntelAsync(mode);
    }

    private async Task DeliverHideoutIntelAsync(string mode)
    {
        if (_hideoutIntelLoading) return;
        _hideoutIntelLoading = true;
        var sequence = _navigationSequence;
        string? json = null;
        string? error = null;
        try
        {
            if (!_hideoutIntelReady.TryGetValue(mode, out json))
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                    _formLifetime.Token);
                cts.CancelAfter(TimeSpan.FromSeconds(16));
                json = await _hideoutIntelCache.GetOrFetchAsync(
                    mode + "-hideout", HideoutIntelMaximumBytes, null,
                    async token =>
                    {
                        using var request = new HttpRequestMessage(
                            HttpMethod.Get,
                            "https://json.tarkov.dev/" + mode + "/hideout");
                        using var response = await HideoutIntelClient.SendAsync(
                            request, HttpCompletionOption.ResponseHeadersRead,
                            token);
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength is > HideoutIntelMaximumBytes)
                            throw new InvalidDataException("Hideout response too large");
                        return await response.Content.ReadAsStringAsync(token);
                    }, cts.Token);

                // No success claim for a syntactically valid but unrelated
                // endpoint document or obsolete schema.
                using var document = JsonDocument.Parse(json);
                var data = document.RootElement.GetProperty("data");
                if (!((data.TryGetProperty("hideoutStations", out var stations)
                          && stations.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                      || (data.TryGetProperty("hideout", out var hideout)
                          && hideout.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                      || (data.TryGetProperty("stations", out var alternate)
                          && alternate.ValueKind is JsonValueKind.Object or JsonValueKind.Array)))
                    throw new InvalidDataException("Unsupported hideout catalogue structure");
                _hideoutIntelReady[mode] = json;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or
            OperationCanceledException or JsonException or IOException or
            InvalidDataException or UnauthorizedAccessException)
        {
            error = "Données de planque indisponibles. Réessaie en ligne; "
                + "les quêtes et objets locaux restent consultables.";
        }
        finally
        {
            _hideoutIntelLoading = false;
        }

        if (_formLifetime.IsCancellationRequested || IsDisposed
            || _navigationInProgress || !_webReady
            || sequence != _navigationSequence
            || !RaidPilotLocalMapHost.IsLocalMapUri(_webView.Source)
            || _webView.CoreWebView2 is not { } core)
            return;
        try
        {
            var payload = json is not null && error is null
                ? "JSON.parse(" + JsonSerializer.Serialize(json) + ")"
                : "null";
            await core.ExecuteScriptAsync(
                "window.RaidPilotLocalMap?.setHideoutIntel?.("
                + payload + ", " + JsonSerializer.Serialize(mode)
                + ", " + JsonSerializer.Serialize(error) + ");");
        }
        catch (Exception ex) when (ex is InvalidOperationException or
            System.Runtime.InteropServices.COMException)
        {
            // Navigation can replace WebView between the validated request
            // and delivery; the next explicit click retries safely.
        }
    }
}
