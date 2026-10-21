using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private bool _localPlayerMarkerUpdateInProgress;
    private (
        ScreenshotObservation Observation,
        string Map,
        int RaidGeneration,
        double UncertaintyMeters,
        bool Snap,
        bool MineAlert,
        double MineDistanceMeters,
        bool BoundaryWarning,
        bool BoundaryCritical,
        double BoundaryDistanceMeters)? _pendingLocalPlayerMarker;

    internal static bool ShouldPublishLocalPlayerMarker(
        bool webReady,
        bool coreReady,
        int updateRaidGeneration,
        int currentRaidGeneration,
        string updateMap,
        string currentMap) =>
        webReady
        && coreReady
        && updateRaidGeneration == currentRaidGeneration
        && string.Equals(updateMap, currentMap, StringComparison.OrdinalIgnoreCase);

    private Task UpdateLocalPlayerMarkerAsync(
        ScreenshotObservation observation,
        double uncertaintyMeters,
        bool snap) =>
        UpdateLocalPlayerMarkerAsync((
            observation,
            _currentMap,
            _raidGeneration,
            uncertaintyMeters,
            snap,
            _mineAlertActive,
            _mineAlertDistanceMeters,
            _boundaryWarningActive,
            _boundaryCriticalActive,
            _boundaryDistanceMeters));

    private async Task UpdateLocalPlayerMarkerAsync((
        ScreenshotObservation Observation,
        string Map,
        int RaidGeneration,
        double UncertaintyMeters,
        bool Snap,
        bool MineAlert,
        double MineDistanceMeters,
        bool BoundaryWarning,
        bool BoundaryCritical,
        double BoundaryDistanceMeters) update)
    {
        var core = _webView.CoreWebView2;
        if (!ShouldPublishLocalPlayerMarker(
                _webReady,
                core is not null,
                update.RaidGeneration,
                _raidGeneration,
                update.Map,
                _currentMap))
        {
            return;
        }

        UpdateCompassHeading(update.Observation.YawDegrees);

        if (_localPlayerMarkerUpdateInProgress)
        {
            _pendingLocalPlayerMarker = update;
            return;
        }

        _localPlayerMarkerUpdateInProgress = true;
        try
        {
            var uncertainty = double.IsFinite(update.UncertaintyMeters)
                ? Math.Clamp(update.UncertaintyMeters, 0.0, 1000.0)
                : 1000.0;
            double? mineDistance = double.IsFinite(
                    update.MineDistanceMeters)
                ? Math.Clamp(update.MineDistanceMeters, 0.0, 10_000.0)
                : null;
            double? boundaryDistance = double.IsFinite(
                    update.BoundaryDistanceMeters)
                ? Math.Clamp(update.BoundaryDistanceMeters, 0.0, 10_000.0)
                : null;
            var payload = JsonSerializer.Serialize(new
            {
                generation = update.RaidGeneration,
                map = update.Map,
                x = update.Observation.X,
                y = update.Observation.Y,
                z = update.Observation.Z,
                yaw = update.Observation.YawDegrees,
                uncertainty,
                snap = update.Snap,
                mineAlert = update.MineAlert,
                mineDistance,
                boundaryWarning = update.BoundaryWarning,
                boundaryCritical = update.BoundaryCritical,
                boundaryDistance,
            });
            await core!.ExecuteScriptAsync(
                $"window.__raidPilotSetLocalPose?.({payload}); void 0;");
        }
        catch
        {
            // The next 10 Hz local pose or navigation restore retries. The
            // local position engine itself must never be stopped by WebView2.
        }
        finally
        {
            _localPlayerMarkerUpdateInProgress = false;
            var pending = _pendingLocalPlayerMarker;
            _pendingLocalPlayerMarker = null;
            if (pending is { } next
                && next.RaidGeneration == _raidGeneration
                && string.Equals(next.Map, _currentMap, StringComparison.OrdinalIgnoreCase))
            {
                _ = UpdateLocalPlayerMarkerAsync(next);
            }
        }
    }

    private async Task RestoreLocalPlayerMarkerAsync()
    {
        if (_lastObservation is not { } observation)
        {
            await ClearLocalPlayerMarkerAsync(_raidGeneration);
            return;
        }

        var uncertainty = _realtimeEstimator.CurrentPose is { } pose
            ? pose.PositionUncertaintyMeters
            : 0.0;
        await UpdateLocalPlayerMarkerAsync(
            observation,
            uncertainty,
            snap: true);
    }

    private async Task ClearLocalPlayerMarkerAsync(int raidGeneration)
    {
        _pendingLocalPlayerMarker = null;
        _compassOverlay.ClearHeading();
        var core = _webView.CoreWebView2;
        if (core is null)
        {
            return;
        }

        try
        {
            await core.ExecuteScriptAsync(
                $"window.__raidPilotClearLocalPose?.({raidGeneration}); void 0;");
        }
        catch
        {
            // A navigation tears down the in-page marker automatically.
        }
    }
}