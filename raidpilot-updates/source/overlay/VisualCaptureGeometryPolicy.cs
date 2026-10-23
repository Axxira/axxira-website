namespace RaidPilot.Overlay;

/// <summary>
/// Reconciles EFT client dimensions with the Windows capture item. Mismatched
/// borders and stretched frames are rejected instead of corrupting yaw.
/// </summary>
internal readonly record struct VisualViewportGeometry(
    int ClientWidth, int ClientHeight, int FrameWidth, int FrameHeight)
{
    internal double AspectRatio => ClientWidth / (double)ClientHeight;
    internal string Description => ClientWidth + "x" + ClientHeight
        + " (aspect " + AspectRatio.ToString("F3") + ")"
        + " capture " + FrameWidth + "x" + FrameHeight;
}

internal static class VisualCaptureGeometryPolicy
{
    internal const double MaximumAspectDifferenceFraction = 0.04;

    internal static bool TryMatchViewport(
        int clientWidth, int clientHeight,
        int capturedWidth, int capturedHeight,
        out VisualViewportGeometry viewport)
    {
        viewport = default;
        if (clientWidth < 160 || clientHeight < 90
            || capturedWidth < 160 || capturedHeight < 90
            || clientWidth > 16384 || clientHeight > 16384
            || capturedWidth > 16384 || capturedHeight > 16384)
            return false;

        var clientAspect = clientWidth / (double)clientHeight;
        var capturedAspect = capturedWidth / (double)capturedHeight;

        // DPI scaling or thin title bars can affect dimensions, but large
        // aspect disagreements cannot produce a defensible yaw measurement.
        if (!double.IsFinite(clientAspect)
            || !double.IsFinite(capturedAspect)
            || clientAspect is < 0.6 or > 5.0
            || Math.Abs((capturedAspect / clientAspect) - 1.0)
                > MaximumAspectDifferenceFraction)
            return false;

        viewport = new(clientWidth, clientHeight, capturedWidth, capturedHeight);
        return true;
    }

    internal static double CalculateEffectiveHorizontalFov(
        int clientWidth, int clientHeight,
        double verticalFovDegrees,
        VisualMotionGateSettings? settings = null)
    {
        var capture = VisualMotionGate.CalculateCaptureRegion(
            clientWidth, clientHeight, settings);
        return EftGameViewSettings.CalculateEffectiveHorizontalFieldOfViewDegrees(
            verticalFovDegrees,
            clientWidth / (double)clientHeight,
            capture.Width / (double)clientWidth);
    }
}
