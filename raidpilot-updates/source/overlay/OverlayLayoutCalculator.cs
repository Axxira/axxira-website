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

    internal static Rectangle CalculateBounds(Rectangle workingArea, int effectiveDpi, bool questContextVisible = false)
    {
        var dpi = effectiveDpi > 0 ? effectiveDpi : LogicalDpi;
        var width = ScaleDip(BaseWidthDip + AdditionalWidthDip, dpi);
        var questFooter = questContextVisible ? ScaleDip(QuestContextHeightDip, dpi) : 0;
        var height = ScaleDip(TotalHeightDip, dpi) + questFooter;
        var rightMargin = ScaleDip(RightMarginDip, dpi);
        var topMargin = ScaleDip(TopMarginDip, dpi);

        return new Rectangle(
            workingArea.Right - width - rightMargin,
            workingArea.Top + topMargin,
            width,
            height);
    }

    private static int ScaleDip(int value, int dpi) =>
        (int)Math.Round(
            value * (dpi / (double)LogicalDpi),
            MidpointRounding.AwayFromZero);
}
