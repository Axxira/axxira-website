namespace RaidPilot.Overlay;

internal static class OverlayLayoutCalculator
{
    internal const int LogicalDpi = 96;
    internal const int BaseWidthDip = 640;
    // Fixed extraction header, unchanged map area, optional quest footer. Anchored top-right.
    internal const int BaseHeightDip = 340;
    internal const int GuidanceHeightDip = 72;
    internal const int QuestContextHeightDip = 96;
    internal const int TotalHeightDip = BaseHeightDip + GuidanceHeightDip;
    internal const int RightMarginDip = 16;
    internal const int TopMarginDip = 16;
    internal const int AdditionalWidthDip = 2 * LogicalDpi;

    internal const int MinimumManualScalePercent = 50;
    internal const int MaximumManualScalePercent = 120;

    // In automatic mode the overlay stays comfortably usable on small screens
    // instead of merely "fitting". Width is the limiting dimension for the
    // 16:9 displays used most often with Tarkov.
    private const double AutomaticMaximumWidthFraction = 0.55;
    private const double AutomaticMaximumHeightFraction = 0.72;
    private const double AutomaticMinimumScale = 0.50;

    internal static Rectangle CalculateBounds(
        Rectangle workingArea,
        int effectiveDpi,
        bool questContextVisible = false,
        double requestedScale = 1.0)
    {
        var dpi = effectiveDpi > 0 ? effectiveDpi : LogicalDpi;
        var scale = ResolveScale(
            workingArea,
            dpi,
            questContextVisible,
            requestedScale);

        var width = ScaleDip(BaseWidthDip + AdditionalWidthDip, dpi);
        var questFooter = questContextVisible ? ScaleDip(QuestContextHeightDip, dpi) : 0;
        var height = ScaleDip(TotalHeightDip, dpi) + questFooter;
        var rightMargin = ScaleDip(RightMarginDip, dpi);
        var topMargin = ScaleDip(TopMarginDip, dpi);

        width = Math.Max(1, ScalePixelLength(width, scale));
        height = Math.Max(1, ScalePixelLength(height, scale));

        return new Rectangle(
            workingArea.Right - width - rightMargin,
            workingArea.Top + topMargin,
            width,
            height);
    }

    internal static double CalculateAutomaticScale(
        Rectangle workingArea,
        int effectiveDpi,
        bool questContextVisible = false)
    {
        var dpi = effectiveDpi > 0 ? effectiveDpi : LogicalDpi;
        var baseWidth = ScaleDip(BaseWidthDip + AdditionalWidthDip, dpi);
        var questFooter = questContextVisible ? ScaleDip(QuestContextHeightDip, dpi) : 0;
        var baseHeight = ScaleDip(TotalHeightDip, dpi) + questFooter;

        if (workingArea.Width <= 0 || workingArea.Height <= 0)
        {
            return 1.0;
        }

        var widthScale = workingArea.Width * AutomaticMaximumWidthFraction / baseWidth;
        var heightScale = workingArea.Height * AutomaticMaximumHeightFraction / baseHeight;
        var preferred = Math.Min(1.0, Math.Min(widthScale, heightScale));
        preferred = Math.Max(AutomaticMinimumScale, preferred);

        return ResolveScale(workingArea, dpi, questContextVisible, preferred);
    }

    internal static double ResolveScale(
        Rectangle workingArea,
        int effectiveDpi,
        bool questContextVisible,
        double requestedScale)
    {
        var dpi = effectiveDpi > 0 ? effectiveDpi : LogicalDpi;
        var scale = double.IsFinite(requestedScale)
            ? Math.Clamp(requestedScale, 0.25, 1.50)
            : 1.0;

        var baseWidth = ScaleDip(BaseWidthDip + AdditionalWidthDip, dpi);
        var questFooter = questContextVisible ? ScaleDip(QuestContextHeightDip, dpi) : 0;
        var baseHeight = ScaleDip(TotalHeightDip, dpi) + questFooter;
        var rightMargin = ScaleDip(RightMarginDip, dpi);
        var topMargin = ScaleDip(TopMarginDip, dpi);
        var safetyMargin = ScaleDip(8, dpi);

        var availableWidth = Math.Max(1, workingArea.Width - rightMargin - safetyMargin);
        var availableHeight = Math.Max(1, workingArea.Height - topMargin - safetyMargin);
        var fitScale = Math.Min(
            availableWidth / (double)Math.Max(1, baseWidth),
            availableHeight / (double)Math.Max(1, baseHeight));

        if (!double.IsFinite(fitScale) || fitScale <= 0)
        {
            return scale;
        }

        // Manual enlargement is always respected only as far as the monitor
        // allows. The overlay therefore never extends beyond the work area.
        return Math.Max(0.10, Math.Min(scale, fitScale));
    }

    private static int ScalePixelLength(int value, double scale) =>
        (int)Math.Round(
            value * scale,
            MidpointRounding.AwayFromZero);

    private static int ScaleDip(int value, int dpi) =>
        (int)Math.Round(
            value * (dpi / (double)LogicalDpi),
            MidpointRounding.AwayFromZero);
}
