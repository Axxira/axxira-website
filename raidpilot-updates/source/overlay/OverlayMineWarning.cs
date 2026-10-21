using System.Diagnostics;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private readonly TarkovDevMineProvider _mineProvider = new();
    private IReadOnlyList<MineHazardZone> _mineHazardZones =
        Array.Empty<MineHazardZone>();
    private string _mineHazardMap = string.Empty;
    private string _mineHazardRequestedMap = string.Empty;
    private bool _mineHazardLoadInProgress;
    private long _mineHazardLastFailureAt;
    private bool _mineAlertActive;
    private double _mineAlertDistanceMeters = double.PositiveInfinity;

    private void UpdateMineProximity(RealtimePose pose)
    {
        if (!string.Equals(
                _mineHazardMap,
                _currentMap,
                StringComparison.OrdinalIgnoreCase))
        {
            _mineAlertActive = false;
            _mineAlertDistanceMeters = double.PositiveInfinity;
            EnsureMineHazardsLoaded();
            return;
        }

        var proximity = TarkovDevMineProvider.EvaluateProximity(
            pose,
            _mineHazardZones,
            _mineAlertActive);
        _mineAlertActive = proximity.Critical;
        _mineAlertDistanceMeters = proximity.DistanceMeters;
    }

    private void EnsureMineHazardsLoaded()
    {
        var map = _currentMap;
        if (string.IsNullOrWhiteSpace(map)
            || string.Equals(
                _mineHazardMap,
                map,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_mineHazardLoadInProgress)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_mineHazardLastFailureAt != 0
            && (now - _mineHazardLastFailureAt)
                / (double)Stopwatch.Frequency < 30.0)
        {
            return;
        }

        _mineHazardLoadInProgress = true;
        _mineHazardRequestedMap = map;
        var generation = _raidGeneration;
        _ = LoadMineHazardsAsync(map, generation);
    }

    private async Task LoadMineHazardsAsync(
        string map,
        int raidGeneration)
    {
        IReadOnlyList<MineHazardZone>? zones = null;
        var failed = false;
        try
        {
            zones = await _mineProvider
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
                        _mineHazardRequestedMap,
                        map,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _mineHazardLoadInProgress = false;
                }

                if (failed)
                {
                    _mineHazardLastFailureAt = Stopwatch.GetTimestamp();
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

                _mineHazardZones = zones;
                _mineHazardMap = map;
                _mineHazardLastFailureAt = 0;

                if (_realtimeEstimator.CurrentPose is { } pose)
                {
                    UpdateMineProximity(pose);
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

    private void ResetMineAlertForRaid()
    {
        _mineAlertActive = false;
        _mineAlertDistanceMeters = double.PositiveInfinity;
        if (!string.Equals(
                _mineHazardMap,
                _currentMap,
                StringComparison.OrdinalIgnoreCase))
        {
            _mineHazardZones = Array.Empty<MineHazardZone>();
            _mineHazardMap = string.Empty;
        }
    }
}