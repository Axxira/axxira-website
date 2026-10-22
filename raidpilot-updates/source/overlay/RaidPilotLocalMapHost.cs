using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

/// <summary>
/// Local-first map host for RaidPilot.
/// Reuses the existing map WebView2 control, redirects tarkov.dev map navigations to the local renderer,
/// and provides a typed bridge for player/team/floor/marker updates.
/// </summary>
public sealed class RaidPilotLocalMapHost : IDisposable
{
    private WebView2? _webView;
    private CoreWebView2? _attachedCore;
    private Form? _owner;
    private string? _pendingMap;
    private string? _lastSnapshotJson;
    private bool _ready;
    private bool _disposed;
    private bool _localNavigationQueued;
    private int _recoveryInProgress;
    private readonly string _localRoot;
    private const string VirtualHost = "raidpilot-map.local";
    private const string LocalUrl = "https://raidpilot-map.local/index.html";

    public event EventHandler<string>? LocalMapMessage;
    public event EventHandler? Ready;
    public bool IsAttached => _attachedCore is not null && !_disposed;
    public static bool IsLocalMapUri(Uri? uri) => uri is not null &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 &&
        uri.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.Equals("/index.html", StringComparison.OrdinalIgnoreCase);
    public static string GetMapUrl(string map) => LocalUrl + "?map=" + Uri.EscapeDataString(NormalizeMapKey(map));

    public RaidPilotLocalMapHost(string? localRoot = null)
    {
        _localRoot = localRoot ?? Path.Combine(AppContext.BaseDirectory, "LocalMap");
    }

    public Task<bool> AttachAsync(Form form)
    {
        var webView = FindFirstWebView(form);
        return webView is null ? Task.FromResult(false) : AttachAsync(webView);
    }

    public async Task<bool> AttachAsync(WebView2 webView)
    {
        if (_disposed) return false;
        if (_attachedCore is not null && ReferenceEquals(_attachedCore, webView.CoreWebView2)) return true;
        if (_webView is not null && !ReferenceEquals(_webView, webView))
            throw new InvalidOperationException("LocalMap host is already attached to another WebView2.");
        _webView = webView;

        if (!Directory.Exists(_localRoot))
            throw new DirectoryNotFoundException($"RaidPilot LocalMap folder not found: {_localRoot}");

        _pendingMap = TryExtractMapKey(_webView.Source?.ToString(), out var initial) ? initial : null;

        // Respect the environment created by OverlayForm; never initialize it twice.
        if (_webView.CoreWebView2 is null) await _webView.EnsureCoreWebView2Async();
        if (_disposed) return false;
        var core = _webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 initialization did not produce a core.");
        if (ReferenceEquals(_attachedCore, core)) return true;
        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            _localRoot,
            CoreWebView2HostResourceAccessKind.Allow);
        var existingAssets = Path.Combine(AppContext.BaseDirectory, "OfflineMaps");
        if (Directory.Exists(existingAssets))
            core.SetVirtualHostNameToFolderMapping("raidpilot-assets.local", existingAssets, CoreWebView2HostResourceAccessKind.Allow);

        core.NavigationStarting += Core_NavigationStarting;
        core.NavigationCompleted += Core_NavigationCompleted;
        core.WebMessageReceived += Core_WebMessageReceived;
        core.ProcessFailed += Core_ProcessFailed;
        _attachedCore = core;
        _owner = webView.FindForm();
        if (_owner is not null) _owner.FormClosed += Owner_FormClosed;

        // The local renderer owns the map surface. Future remote map navigations are converted to setMap().
        _webView.Source = new Uri(LocalUrl);
        return true;
    }

    public async Task SetMapAsync(string mapKey)
    {
        _pendingMap = NormalizeMapKey(mapKey);
        if (_ready) await PostAsync("set-map", new { map = _pendingMap });
    }

    public Task SetPlayerAsync(object player) => PostAsync("player", player);
    public Task SetTeamAsync(object teammates) => PostAsync("team", teammates);
    public Task SetFloorAsync(string floor) => PostAsync("floor", new { floor });
    public Task SetMarkersAsync(object markers) => PostAsync("markers", markers);
    public Task SetCalibrationAsync(object calibration) => PostAsync("calibration", calibration);

    public async Task ApplySnapshotAsync(object snapshot)
    {
        _lastSnapshotJson = JsonSerializer.Serialize(snapshot);
        if (!_ready || _webView?.CoreWebView2 is null) return;
        _webView.CoreWebView2.PostWebMessageAsJson(
            JsonSerializer.Serialize(new { type = "snapshot", payload = snapshot }));
        await Task.CompletedTask;
    }

    public async Task<string?> ReadHealthAsync()
    {
        if (_webView?.CoreWebView2 is null || !_ready) return null;
        var json = await _webView.CoreWebView2.ExecuteScriptAsync(
            "JSON.stringify(window.RaidPilotLocalMap?.health?.() ?? null)");
        try
        {
            return JsonSerializer.Deserialize<string>(json);
        }
        catch
        {
            return json;
        }
    }

    private Task PostAsync(string type, object payload)
    {
        if (_disposed || !_ready || _webView?.CoreWebView2 is null)
            return Task.CompletedTask;

        var json = JsonSerializer.Serialize(new { type, payload });
        _webView.CoreWebView2.PostWebMessageAsJson(json);
        return Task.CompletedTask;
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_disposed) return;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var destination) && IsLocalMapUri(destination))
        {
            _ready = false;
            if (TryExtractMapKey(e.Uri, out var selected)) _pendingMap = selected;
            return;
        }

        if (TryExtractMapKey(e.Uri, out var mapKey))
        {
            e.Cancel = true;
            _pendingMap = mapKey;
            if (_ready)
                _ = SetMapAsync(mapKey);
            else if (!_localNavigationQueued && _webView is { IsDisposed: false, IsHandleCreated: true } view)
            {
                // An immediate legacy Navigate() can supersede our initial Source
                // assignment. Resume the local page after the cancelled navigation.
                _localNavigationQueued = true;
                view.BeginInvoke((Action)(() =>
                {
                    _localNavigationQueued = false;
                    if (!_disposed && !view.IsDisposed && !_ready)
                        view.CoreWebView2?.Navigate(LocalUrl);
                }));
            }
            return;
        }

        // A map WebView should never navigate away during a raid. Keep links isolated from the map surface.
        if (_ready && Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            e.Cancel = true;
        }
    }

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // Document completion does not imply fetch(cache/*.json) has completed.
        // The renderer explicitly signals readiness after its cache and bridge load.
        if (!e.IsSuccess) _ready = false;
    }

    private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed || !IsLocalMapUri(_webView?.Source)) return;
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch { message = e.WebMessageAsJson; }
        try
        {
            using var document = JsonDocument.Parse(message);
            if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "raidpilot-local-ready")
            {
                _ready = true;
                if (!string.IsNullOrWhiteSpace(_pendingMap)) _ = SetMapAsync(_pendingMap);
                if (!string.IsNullOrWhiteSpace(_lastSnapshotJson))
                    _webView!.CoreWebView2!.PostWebMessageAsJson($"{{\"type\":\"snapshot\",\"payload\":{_lastSnapshotJson}}}");
                Ready?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (JsonException) { }
        LocalMapMessage?.Invoke(this, message);
    }

    private async void Core_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _recoveryInProgress, 1) != 0) return;
        try
        {
            _ready = false;
            await Task.Delay(250);
            if (_webView is not null && !_disposed)
                _webView.Source = new Uri(LocalUrl);
        }
        finally
        {
            Interlocked.Exchange(ref _recoveryInProgress, 0);
        }
    }

    private static WebView2? FindFirstWebView(Control root)
    {
        if (root is WebView2 wv) return wv;
        foreach (Control child in root.Controls)
        {
            var found = FindFirstWebView(child);
            if (found is not null) return found;
        }
        return null;
    }

    public static bool TryExtractMapKey(string? uriText, out string mapKey)
    {
        mapKey = "";
        if (string.IsNullOrWhiteSpace(uriText)) return false;
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return false;

        // Preserve map selection when the existing overlay chooses its older offline renderer.
        if (IsLocalMapUri(uri) || (uri.Host.Equals("raidpilot.local", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.Equals("/OfflineMap.html", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && parts[0].Equals("map", StringComparison.OrdinalIgnoreCase))
                {
                    mapKey = NormalizeMapKey(Uri.UnescapeDataString(parts[1]));
                    return !string.IsNullOrWhiteSpace(mapKey);
                }
            }
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var idx = Array.FindIndex(segments, s => s.Equals("map", StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx + 1 >= segments.Length) return false;

        mapKey = NormalizeMapKey(Uri.UnescapeDataString(segments[idx + 1]));
        return !string.IsNullOrWhiteSpace(mapKey);
    }

    public static string NormalizeMapKey(string key)
    {
        var v = (key ?? "").Trim().ToLowerInvariant().Replace('_', '-');
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["streets"] = "streets-of-tarkov",
            ["streetsoftarkov"] = "streets-of-tarkov",
            ["streets-2d"] = "streets-of-tarkov",
            ["streets-3d"] = "streets-of-tarkov",
            ["groundzero"] = "ground-zero",
            ["ground-zero-21"] = "ground-zero",
            ["night-factory"] = "factory",
            ["factory-2d"] = "factory",
            ["factory-3d"] = "factory",
            ["labs"] = "the-lab",
            ["lab"] = "the-lab",
            ["the-lab-dark"] = "the-lab",
            ["labyrinth"] = "the-labyrinth",
            ["customs-2d"] = "customs",
            ["customs-3d"] = "customs",
            ["reserve-2d"] = "reserve",
            ["reserve-3d"] = "reserve",
            ["shoreline-2d"] = "shoreline",
            ["shoreline-3d"] = "shoreline",
            ["woods-2d"] = "woods",
            ["woods-3d"] = "woods",
            ["interchange-2d"] = "interchange",
            ["interchange-3d"] = "interchange",
            ["lighthouse-2d"] = "lighthouse",
            ["lighthouse-3d"] = "lighthouse",
            ["terminal-2d"] = "terminal"
        };
        return aliases.TryGetValue(v, out var mapped) ? mapped : v;
    }

    private void Owner_FormClosed(object? sender, FormClosedEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_owner is not null) _owner.FormClosed -= Owner_FormClosed;
        _owner = null;
        if (_attachedCore is { } core)
        {
            core.NavigationStarting -= Core_NavigationStarting;
            core.NavigationCompleted -= Core_NavigationCompleted;
            core.WebMessageReceived -= Core_WebMessageReceived;
            core.ProcessFailed -= Core_ProcessFailed;
        }
        _webView = null;
        _attachedCore = null;
    }
}
