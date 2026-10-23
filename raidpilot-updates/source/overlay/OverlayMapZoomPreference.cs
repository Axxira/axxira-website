using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace RaidPilot.Overlay;

/// <summary>
/// Trusted zoom-preference bridge. Only approved map pages can publish a
/// bounded zoom factor. No arbitrary WebView messages are interpreted.
/// </summary>
internal sealed partial class OverlayForm
{
    private double? _preferredMapZoomFactor = OverlayPreferencesStore.LoadMapZoomFactor();

    internal static bool TryParseMapZoomPreference(
        string? pageUri, string? json, out double factor)
    {
        factor = 0;
        if (string.IsNullOrWhiteSpace(json)
            || json.Length > 256
            || !WebNavigationPolicy.TryGetAllowedMapUri(pageUri, out var source)
            || source is null)
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "raidpilot-map-zoom-preference"
                || !root.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("factor", out var value)
                || value.ValueKind != JsonValueKind.Number
                || !value.TryGetDouble(out var proposed)
                || !double.IsFinite(proposed)
                || proposed is < OverlayPreferencesStore.MinimumMapZoomFactor
                    or > OverlayPreferencesStore.MaximumMapZoomFactor)
                return false;

            factor = proposed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void OnMapZoomPreferenceMessage(
        object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_formLifetime.IsCancellationRequested || IsDisposed
            || !TryParseMapZoomPreference(
                args.Source, args.WebMessageAsJson, out var factor)
            || !Uri.TryCreate(args.Source, UriKind.Absolute, out var source)
            || _webView.Source is not { } currentPage
            || Uri.Compare(source, currentPage,
                UriComponents.SchemeAndServer | UriComponents.Path,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) != 0)
            return;

        if (_preferredMapZoomFactor is { } previous
            && Math.Abs(previous - factor) < 1e-7)
            return;

        if (OverlayPreferencesStore.TrySaveMapZoomFactor(factor))
            _preferredMapZoomFactor = factor;
    }
}
