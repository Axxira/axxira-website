using System.Diagnostics;

namespace RaidPilot.Overlay;

internal sealed partial class OverlayForm
{
    private const double LootHideDebounceSeconds = 0.10;
    private const double LootRestoreDebounceSeconds = 0.16;

    private readonly CompassOverlayForm _compassOverlay = new();
    private bool _raidActive;
    private bool _lootUiVisible;
    private bool _manualOverlayHidden;
    private long _lootCursorVisibleSince;
    private long _lootCursorHiddenSince;

    private void SetRaidActive(bool active)
    {
        _raidActive = active;
        _lootUiVisible = false;
        _lootCursorVisibleSince = 0;
        _lootCursorHiddenSince = 0;
        ApplyAutomaticOverlayVisibility();
    }

    private void UpdateAutomaticOverlayVisibility(
        bool tarkovForeground,
        bool tarkovCursorVisible,
        long timestamp)
    {
        if (!_raidActive)
        {
            _lootUiVisible = false;
            _lootCursorVisibleSince = 0;
            _lootCursorHiddenSince = 0;
            ApplyAutomaticOverlayVisibility();
            return;
        }

        if (tarkovForeground)
        {
            if (tarkovCursorVisible)
            {
                _lootCursorHiddenSince = 0;
                if (_lootCursorVisibleSince == 0)
                {
                    _lootCursorVisibleSince = timestamp;
                }
                if (!_lootUiVisible
                    && SecondsSince(_lootCursorVisibleSince, timestamp)
                        >= LootHideDebounceSeconds)
                {
                    _lootUiVisible = true;
                }
            }
            else
            {
                _lootCursorVisibleSince = 0;
                if (_lootCursorHiddenSince == 0)
                {
                    _lootCursorHiddenSince = timestamp;
                }
                if (_lootUiVisible
                    && SecondsSince(_lootCursorHiddenSince, timestamp)
                        >= LootRestoreDebounceSeconds)
                {
                    _lootUiVisible = false;
                }
            }
        }

        ApplyAutomaticOverlayVisibility();
    }

    private void ApplyAutomaticOverlayVisibility()
    {
        var mapShouldBeVisible =
            !_manualOverlayHidden
            && (!_clickThrough || (_raidActive && !_lootUiVisible));

        if (mapShouldBeVisible)
        {
            if (!Visible)
            {
                ShowNoActivate(force: true);
            }
        }
        else if (Visible && _clickThrough)
        {
            Hide();
        }

        var compassShouldBeVisible =
            !_manualOverlayHidden
            && _raidActive
            && !_lootUiVisible
            && _clickThrough;
        _compassOverlay.SetCompassVisible(compassShouldBeVisible);
    }

    private void SetManualOverlayHidden(bool hidden)
    {
        _manualOverlayHidden = hidden;
        ApplyAutomaticOverlayVisibility();
    }

    private void UpdateCompassHeading(
        double yawDegrees)
    {
        _compassOverlay.UpdateHeading(
            yawDegrees,
            GetTargetWorkingArea());
        _compassOverlay.SetCompassVisible(
            !_manualOverlayHidden
            && _raidActive
            && !_lootUiVisible
            && _clickThrough);
    }

    private static double SecondsSince(long start, long end) =>
        start <= 0 || end <= start
            ? 0
            : (end - start) / (double)Stopwatch.Frequency;

    internal static bool ShouldTreatCursorAsLootUi(
        bool raidActive,
        bool tarkovForeground,
        bool cursorVisible) =>
        raidActive && tarkovForeground && cursorVisible;
}