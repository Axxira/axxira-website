using System.Diagnostics;

namespace RaidPilot.Overlay;

/// <summary>
/// An explicit, single-use, short-lived user request to scan the NEXT
/// manual EFT F8 screenshot. Auto-reanchors must never be scanned. All
/// timestamps use the monotonic Stopwatch clock to reject old captures.
/// </summary>
internal sealed class StashPilotF8Gate
{
    private readonly long _timeoutTicks = 90 * Stopwatch.Frequency;
    private long _armedAt;
    internal bool IsArmed => _armedAt > 0;

    internal void Arm(long now)
    {
        if (now <= 0) throw new ArgumentOutOfRangeException(nameof(now));
        _armedAt = now;
    }

    internal void Cancel() => _armedAt = 0;

    internal bool TryConsume(long now, bool isAutomaticCapture, bool screenshotPathVerified)
    {
        if (_armedAt <= 0) return false;
        if (now < _armedAt || now - _armedAt > _timeoutTicks)
        {
            Cancel();
            return false;
        }
        if (isAutomaticCapture || !screenshotPathVerified)
            return false;
        Cancel();
        return true;
    }
}
