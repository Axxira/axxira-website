using System.Drawing.Drawing2D;

namespace RaidPilot.Overlay;

/// <summary>
/// Boussole plein-écran indépendante de la fenêtre de carte. Elle reste
/// centrée sur le moniteur EFT même lorsque la minimap est déplacée ou
/// redimensionnée.
/// </summary>
internal sealed class CompassOverlayForm : Form
{
    internal const int CompassWidthDip = 900;
    internal const int CompassHeightDip = 104;
    internal const int TopMarginDip = 10;
    internal const int DegreesVisibleEachSide = 60;
    internal const int TickStepDegrees = 5;
    internal const double MinimumHeadingRepaintDegrees = 0.08;

    private double _yawDegrees;
    private Rectangle _targetArea;
    private bool _desiredVisible;
    private bool _hasHeading;

    internal CompassOverlayForm()
    {
        Text = "RaidPilot Compass";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= (int)(
                NativeMethods.WsExToolWindow
                | NativeMethods.WsExTransparent
                | NativeMethods.WsExNoActivate);
            return parameters;
        }
    }

    internal void UpdateHeading(double yawDegrees, Rectangle targetArea)
    {
        if (!double.IsFinite(yawDegrees)
            || targetArea.Width <= 0
            || targetArea.Height <= 0)
        {
            return;
        }

        var normalizedYaw = Normalize360(yawDegrees);
        var targetChanged = targetArea != _targetArea;
        var headingChanged = !_hasHeading
            || AngularDistance(_yawDegrees, normalizedYaw)
                >= MinimumHeadingRepaintDegrees;

        _yawDegrees = normalizedYaw;
        _targetArea = targetArea;
        _hasHeading = true;

        if (targetChanged)
        {
            PositionForTarget();
        }

        if (headingChanged || targetChanged)
        {
            Invalidate();
        }
    }

    internal void SetCompassVisible(bool visible)
    {
        _desiredVisible = visible;
        if (!visible)
        {
            if (Visible)
            {
                Hide();
            }
            return;
        }

        if (!_hasHeading
            || _targetArea.Width <= 0
            || _targetArea.Height <= 0)
        {
            return;
        }

        if (Visible)
        {
            return;
        }

        PositionForTarget();
        Show();
        NativeMethods.ShowWindow(Handle, NativeMethods.SwShowNoActivate);
        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HwndTopMost,
            Left,
            Top,
            Width,
            Height,
            NativeMethods.SwpNoActivate);
        Invalidate();
    }

    internal bool DesiredVisible => _desiredVisible;

    internal void ClearHeading()
    {
        _hasHeading = false;
        SetCompassVisible(false);
    }

    private void PositionForTarget()
    {
        if (_targetArea.Width <= 0 || _targetArea.Height <= 0)
        {
            return;
        }

        var dpi = NativeMethods.GetDpiForSystem();
        var effectiveDpi = dpi > 0 ? (int)dpi : 96;
        var scale = effectiveDpi / 96.0;
        var desiredWidth = (int)Math.Round(CompassWidthDip * scale);
        var desiredHeight = (int)Math.Round(CompassHeightDip * scale);
        var width = Math.Min(
            Math.Max((int)Math.Round(520 * scale), desiredWidth),
            Math.Max(1, _targetArea.Width - 40));
        var height = Math.Min(
            desiredHeight,
            Math.Max(1, _targetArea.Height / 4));
        var topMargin = (int)Math.Round(TopMarginDip * scale);

        Bounds = new Rectangle(
            _targetArea.Left + ((_targetArea.Width - width) / 2),
            _targetArea.Top + topMargin,
            width,
            height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var width = ClientSize.Width;
        var height = ClientSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var centerX = width / 2f;
        var centerY = 54f;
        var pixelsPerDegree = width / (DegreesVisibleEachSide * 2.0);
        var firstDegree = (int)Math.Floor(
            (_yawDegrees - DegreesVisibleEachSide) / TickStepDegrees)
            * TickStepDegrees;
        var lastDegree = (int)Math.Ceiling(
            (_yawDegrees + DegreesVisibleEachSide) / TickStepDegrees)
            * TickStepDegrees;

        using var shadowPen = new Pen(Color.FromArgb(190, 0, 0, 0), 4f);
        using var tickPen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.6f);
        using var majorPen = new Pen(Color.White, 2.3f);
        using var centerPen = new Pen(Color.White, 2.8f);
        using var degreeFont = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
        using var cardinalFont = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Point);
        using var headingFont = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Point);
        using var whiteBrush = new SolidBrush(Color.White);
        using var minorBrush = new SolidBrush(Color.White);
        using var accentBrush = new SolidBrush(Color.White);
        using var shadowBrush = new SolidBrush(Color.FromArgb(215, 0, 0, 0));

        for (var degree = firstDegree;
             degree <= lastDegree;
             degree += TickStepDegrees)
        {
            var delta = degree - _yawDegrees;
            var x = centerX + (float)(delta * pixelsPerDegree);
            if (x < 8 || x > width - 8)
            {
                continue;
            }

            var normalized = Normalize360(degree);
            var rounded = (int)Math.Round(normalized) % 360;
            var cardinal = CardinalLabel(rounded);
            var major = rounded % 15 == 0;
            var tickHeight = cardinal is not null ? 22f : major ? 17f : 10f;
            var yBottom = centerY + 15f;
            var yTop = yBottom - tickHeight;

            graphics.DrawLine(
                shadowPen,
                x + 1,
                yTop + 1,
                x + 1,
                yBottom + 1);
            graphics.DrawLine(
                cardinal is not null || major ? majorPen : tickPen,
                x,
                yTop,
                x,
                yBottom);

            var label = cardinal
                ?? (major ? rounded.ToString("000") : null);
            if (label is null)
            {
                continue;
            }

            var font = cardinal is not null ? cardinalFont : degreeFont;
            var size = graphics.MeasureString(label, font);
            var textX = x - (size.Width / 2f);
            var textY = yTop - size.Height - 2;
            graphics.DrawString(
                label,
                font,
                shadowBrush,
                textX + 1.5f,
                textY + 1.5f);
            graphics.DrawString(
                label,
                font,
                cardinal is not null ? whiteBrush : minorBrush,
                textX,
                textY);
        }

        // Repère central fixe PUBG.
        var pointerY = centerY + 18f;
        graphics.DrawLine(
            centerPen,
            centerX,
            pointerY,
            centerX,
            pointerY + 12f);
        var triangle = new[]
        {
            new PointF(centerX - 6f, pointerY + 12f),
            new PointF(centerX + 6f, pointerY + 12f),
            new PointF(centerX, pointerY + 20f),
        };
        graphics.FillPolygon(accentBrush, triangle);

        var heading = $"{Math.Round(_yawDegrees) % 360:000}°";
        var headingSize = graphics.MeasureString(heading, headingFont);
        var headingX = centerX - (headingSize.Width / 2f);
        var headingY = height - headingSize.Height - 2f;
        graphics.DrawString(
            heading,
            headingFont,
            shadowBrush,
            headingX + 1.5f,
            headingY + 1.5f);
        graphics.DrawString(
            heading,
            headingFont,
            accentBrush,
            headingX,
            headingY);
    }

    internal static string? CardinalLabel(int degrees) => degrees switch
    {
        0 => "N",
        45 => "NE",
        90 => "E",
        135 => "SE",
        180 => "S",
        225 => "SO",
        270 => "O",
        315 => "NO",
        _ => null,
    };

    internal static double Normalize360(double degrees)
    {
        var result = degrees % 360.0;
        if (result < 0)
        {
            result += 360.0;
        }
        return result >= 359.999999 ? 0.0 : result;
    }

    internal static double AngularDistance(double a, double b)
    {
        var difference = Math.Abs(Normalize360(a) - Normalize360(b));
        return Math.Min(difference, 360.0 - difference);
    }
}