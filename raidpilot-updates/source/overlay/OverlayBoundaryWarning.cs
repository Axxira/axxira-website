using System.Diagnostics;
using System.Text.Json;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private readonly TarkovDevBoundaryProvider _boundaryProvider = new();
    private IReadOnlyList<BoundaryHazardZone> _boundaryZones =
        Array.Empty<BoundaryHazardZone>();
    private string _boundaryMap = string.Empty;
    private string _boundaryRequestedMap = string.Empty;
    private bool _boundaryLoadInProgress;
    private long _boundaryLastFailureAt;
    private bool _boundaryWarningActive;
    private bool _boundaryCriticalActive;
    private double _boundaryDistanceMeters = double.PositiveInfinity;

    private void UpdateBoundaryProximity(RealtimePose pose)
    {
        if (!string.Equals(
                _boundaryMap,
                _currentMap,
                StringComparison.OrdinalIgnoreCase))
        {
            _boundaryWarningActive = false;
            _boundaryCriticalActive = false;
            _boundaryDistanceMeters = double.PositiveInfinity;
            EnsureBoundariesLoaded();
            return;
        }

        var proximity = TarkovDevBoundaryProvider.EvaluateProximity(
            pose,
            _boundaryZones,
            _boundaryWarningActive,
            _boundaryCriticalActive);

        _boundaryWarningActive = proximity.Warning;
        _boundaryCriticalActive = proximity.Critical;
        _boundaryDistanceMeters = proximity.DistanceMeters;
    }

    private void EnsureBoundariesLoaded()
    {
        var map = _currentMap;
        if (string.IsNullOrWhiteSpace(map)
            || string.Equals(
                _boundaryMap,
                map,
                StringComparison.OrdinalIgnoreCase)
            || _boundaryLoadInProgress)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_boundaryLastFailureAt != 0
            && (now - _boundaryLastFailureAt)
                / (double)Stopwatch.Frequency < 30.0)
        {
            return;
        }

        _boundaryLoadInProgress = true;
        _boundaryRequestedMap = map;
        var generation = _raidGeneration;
        _ = LoadBoundariesAsync(map, generation);
    }

    private async Task LoadBoundariesAsync(
        string map,
        int raidGeneration)
    {
        IReadOnlyList<BoundaryHazardZone>? zones = null;
        var failed = false;
        try
        {
            zones = await _boundaryProvider
                .GetForMapAsync(map, _formLifetime.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (_formLifetime.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            failed = true;
        }
        finally
        {
            RunOnUiThread(() =>
            {
                if (string.Equals(
                        _boundaryRequestedMap,
                        map,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _boundaryLoadInProgress = false;
                }

                if (failed)
                {
                    _boundaryLastFailureAt = Stopwatch.GetTimestamp();
                    return;
                }

                if (raidGeneration != _raidGeneration
                    || !string.Equals(
                        map,
                        _currentMap,
                        StringComparison.OrdinalIgnoreCase)
                    || zones is null)
                {
                    return;
                }

                _boundaryZones = zones;
                _boundaryMap = map;
                _boundaryLastFailureAt = 0;
                _ = PushBoundaryOverlayAsync();

                if (_realtimeEstimator.CurrentPose is { } pose)
                {
                    UpdateBoundaryProximity(pose);
                    if (_lastObservation is { } observation)
                    {
                        _ = UpdateLocalPlayerMarkerAsync(
                            observation,
                            pose.PositionUncertaintyMeters,
                            snap: false);
                    }
                }
            });
        }
    }

    private async Task PushBoundaryOverlayAsync()
    {
        var core = _webView.CoreWebView2;
        if (core is null
            || !_webReady
            || _navigationInProgress
            || _formLifetime.IsCancellationRequested
            || !string.Equals(
                _boundaryMap,
                _currentMap,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            generation = _raidGeneration,
            map = _boundaryMap,
            zones = _boundaryZones.Select(zone => new
            {
                id = zone.Id,
                x = zone.X,
                z = zone.Z,
                outline = zone.Outline.Select(point => new
                {
                    x = point.X,
                    z = point.Z,
                }),
            }),
        });

        try
        {
            await core.ExecuteScriptAsync(
                $"window.__raidPilotSetBoundaryHazards?.({payload}); void 0;");
        }
        catch
        {
        }
    }

    private async Task RestoreBoundaryOverlayAsync()
    {
        if (!string.Equals(
                _boundaryMap,
                _currentMap,
                StringComparison.OrdinalIgnoreCase))
        {
            await ClearBoundaryOverlayAsync(_raidGeneration);
            EnsureBoundariesLoaded();
            return;
        }

        await PushBoundaryOverlayAsync();
    }

    private async Task ClearBoundaryOverlayAsync(int raidGeneration)
    {
        var core = _webView.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            await core.ExecuteScriptAsync(
                $"window.__raidPilotClearBoundaryHazards?.({raidGeneration}); void 0;");
        }
        catch
        {
        }
    }

    private void ResetBoundaryAlertForRaid()
    {
        _boundaryWarningActive = false;
        _boundaryCriticalActive = false;
        _boundaryDistanceMeters = double.PositiveInfinity;

        if (!string.Equals(
                _boundaryMap,
                _currentMap,
                StringComparison.OrdinalIgnoreCase))
        {
            _boundaryZones = Array.Empty<BoundaryHazardZone>();
            _boundaryMap = string.Empty;
            _ = ClearBoundaryOverlayAsync(_raidGeneration);
        }
    }
}