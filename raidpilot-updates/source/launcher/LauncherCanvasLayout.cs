using System.Drawing;

namespace RaidPilot.Launcher;

internal readonly record struct LauncherCanvasTransform(float Scale, float OffsetX, float OffsetY)
{
    internal Point ToCanvas(Point p) => new(
        (int)Math.Floor((p.X - OffsetX) / Scale),
        (int)Math.Floor((p.Y - OffsetY) / Scale));
}

internal static class LauncherCanvasLayout
{
    private const int DesignWidth = 1180;
    private const int DesignHeight = 760;

    internal static Size FitWindow(Size workingArea) => new(
        Math.Clamp(workingArea.Width - 24, 420, DesignWidth),
        Math.Clamp(workingArea.Height - 32, 300, DesignHeight));

    internal static LauncherCanvasTransform Fit(Size size)
    {
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        var scale = Math.Min(width / (float)DesignWidth,
            height / (float)DesignHeight);
        return new LauncherCanvasTransform(scale,
            (width - DesignWidth * scale) * 0.5f,
            (height - DesignHeight * scale) * 0.5f);
    }
}

