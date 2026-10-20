using System.Diagnostics;
using System.Text.Json;

namespace RaidPilot.Overlay;

/// <summary>A vertical observation is not renewed by horizontal dead reckoning.</summary>
internal sealed class AutomaticFloorEvidence
{
    private long _id;
    private FloorAnchor? _anchor;
    internal sealed record FloorAnchor(long Id, string Map, int Generation, double X, double Y, double Z,
        long Timestamp, string Source);
    internal FloorAnchor? Anchor => _anchor;
    internal void Reset() { _anchor = null; }
    internal bool Record(ScreenshotObservation observation, string map, int generation, long capturedAt, string source)
    {
        if (capturedAt <= 0 || source is not ("native" or "visual") || string.IsNullOrWhiteSpace(map)
            || !new[] { observation.X, observation.Y, observation.Z }.All(v => double.IsFinite(v) && Math.Abs(v) <= 20000)) return false;
        if (_anchor is { } previous && previous.Generation == generation && previous.Map == map && capturedAt < previous.Timestamp) return false;
        _anchor = new(++_id, map, generation, observation.X, observation.Y, observation.Z, capturedAt, source);
        return true;
    }
    internal object? GetAnchorPayload(string map, int generation, long now)
    {
        if (_anchor is not { } a || a.Map != map || a.Generation != generation || now < a.Timestamp) return null;
        return new { id = a.Id, x = a.X, y = a.Y, z = a.Z, source = a.Source,
            age = (now - a.Timestamp) / (double)Stopwatch.Frequency };
    }
}

internal sealed partial class OverlayForm
{
    private readonly AutomaticFloorEvidence _floorEvidence = new();
    private readonly System.Windows.Forms.Timer _floorTimer = new() { Interval = 500 };
    private readonly ToolStripMenuItem _automaticFloorMenuItem = new("Niveaux automatiques")
        { Checked = true, CheckOnClick = true };
    private bool _floorUpdateBusy;
    private long _floorSequence;
    private long _floorPoseTimestamp;
    private string _floorStatus = "waiting_anchor";

    private void InitializeAutomaticFloors()
    {
        var menu = _trayIcon.ContextMenuStrip;
        if (menu is not null) menu.Items.Insert(Math.Min(5, menu.Items.Count), _automaticFloorMenuItem);
        _automaticFloorMenuItem.ToolTipText = "Sélection par altitude observée. Aucune capture supplémentaire. Les étages intermédiaires restent estimés.";
        _automaticFloorMenuItem.Click += async (_, _) => await UpdateAutomaticFloorsAsync();
        _floorTimer.Tick += async (_, _) => await UpdateAutomaticFloorsAsync();
        _floorTimer.Start();
    }
    private void RecordAutomaticFloorAnchor(ScreenshotObservation observation, long capturedAt, string source)
    {
        if (_floorEvidence.Record(observation, _currentMap, _raidGeneration, capturedAt, source))
        {
            _floorPoseTimestamp = Stopwatch.GetTimestamp();
            _ = UpdateAutomaticFloorsAsync();
        }
    }
    private void ResetAutomaticFloors()
    {
        _floorEvidence.Reset();_floorPoseTimestamp = 0;_floorStatus = "waiting_anchor";
        var sequence = ++_floorSequence;
        _ = ClearAutomaticFloorsAsync(sequence);
    }
    private async Task ClearAutomaticFloorsAsync(long sequence)
    {
        try
        {
            if (_webView.CoreWebView2 is { } core && !_formLifetime.IsCancellationRequested)
                await core.ExecuteScriptAsync($"window.__raidPilotClearFloors?.({{sequence:{sequence}}}); void 0;");
        }
        catch { /* An unloaded page no longer contains its former floor. */ }
    }
    private async Task UpdateAutomaticFloorsAsync()
    {
        if (_floorUpdateBusy || IsDisposed || _formLifetime.IsCancellationRequested || !_webReady
            || _navigationInProgress || _navigationStartPending || _webView.CoreWebView2 is not { } core) return;
        _floorUpdateBusy = true;
        try
        {
            var now = Stopwatch.GetTimestamp();
            var p = _realtimeEstimator.CurrentPose;
            var valid = p is { } v && new[] { v.X, v.Y, v.Z, v.PositionUncertaintyMeters }.All(double.IsFinite);
            var poseAge = _floorPoseTimestamp > 0 ? (now - _floorPoseTimestamp) / (double)Stopwatch.Frequency : 36000.0;
            var packet = JsonSerializer.Serialize(new {
                sequence = ++_floorSequence, generation = _raidGeneration, map = _currentMap,
                enabled = _automaticFloorMenuItem.Checked,
                active = _controlTrackingActive && _tarkovIsForeground && _liveTrackingEnabled,
                position = valid ? new {x=p!.Value.X,y=p.Value.Y,z=p.Value.Z} : null,
                uncertainty = valid ? p!.Value.PositionUncertaintyMeters : 999,
                poseAge, anchor = _floorEvidence.GetAnchorPayload(_currentMap,_raidGeneration,now)
            });
            var result = await core.ExecuteScriptAsync($"JSON.stringify(window.__raidPilotSetFloorPose?.({packet}) ?? {{status:'loading'}});");
            if (result is not null)
            {
                var inner = JsonSerializer.Deserialize<string>(result);
                if (inner is not null)
                {
                    using var document = JsonDocument.Parse(inner);
                    if (document.RootElement.TryGetProperty("status", out var status)) _floorStatus = status.GetString() ?? "unknown";
                }
            }
        }
        catch(OperationCanceledException) when(_formLifetime.IsCancellationRequested) { }
        catch { _floorStatus = "unavailable"; }
        finally { _floorUpdateBusy = false; }
    }
    private void DisposeAutomaticFloors() { _floorTimer.Stop();_floorTimer.Dispose(); }
}
