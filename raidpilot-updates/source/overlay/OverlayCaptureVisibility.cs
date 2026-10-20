namespace RaidPilot.Overlay;

internal static class OverlayCaptureVisibility
{
    internal static uint GetDisplayAffinity(bool visibleInCaptures) =>
        visibleInCaptures
            ? NativeMethods.WdaNone
            : NativeMethods.WdaExcludeFromCapture;
}
