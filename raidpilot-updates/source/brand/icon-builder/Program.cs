using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var outputDir = Path.GetFullPath(
    args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
Directory.CreateDirectory(outputDir);
var icoPath = Path.Combine(outputDir, "RaidPilot.ico");
var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
var frames = sizes.Select(RenderPng).ToArray();

using (var file = File.Create(icoPath))
using (var writer = new BinaryWriter(file))
{
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)sizes.Length);
    var offset = 6 + (16 * sizes.Length);

    for (var i = 0; i < sizes.Length; i++)
    {
        var size = sizes[i];
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)frames[i].Length);
        writer.Write((uint)offset);
        offset += frames[i].Length;
    }

    foreach (var frame in frames)
    {
        writer.Write(frame);
    }
}

Console.WriteLine(icoPath);

static byte[] RenderPng(int size)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    graphics.Clear(Color.Transparent);

    var s = (float)size;
    using var outer = RoundedRectangle(s * 0.045f, s * 0.045f, s * 0.91f, s * 0.91f, s * 0.19f);
    using var background = new SolidBrush(Color.FromArgb(255, 8, 14, 17));
    using var outerPen = new Pen(Color.FromArgb(255, 38, 63, 71), Math.Max(1f, s * 0.018f));
    graphics.FillPath(background, outer);
    graphics.DrawPath(outerPen, outer);

    var cx = s * 0.5f;
    var cy = s * 0.455f;
    var hexRadius = s * 0.355f;
    var hex = Enumerable.Range(0, 6).Select(i =>
    {
        var angle = (-90 + 60 * i) * Math.PI / 180.0;
        return new PointF(
            cx + (float)Math.Cos(angle) * hexRadius,
            cy + (float)Math.Sin(angle) * hexRadius);
    }).ToArray();

    using var hexBrush = new SolidBrush(Color.FromArgb(255, 13, 23, 27));
    using var hexPen = new Pen(Color.FromArgb(255, 62, 108, 119), Math.Max(1f, s * 0.025f));
    graphics.FillPolygon(hexBrush, hex);
    graphics.DrawPolygon(hexPen, hex);

    var ringRadius = s * 0.205f;
    using var ringPen = new Pen(Color.FromArgb(255, 26, 58, 68), Math.Max(1f, s * 0.018f));
    graphics.DrawEllipse(ringPen, cx - ringRadius, cy - ringRadius, ringRadius * 2, ringRadius * 2);

    using var tickPen = new Pen(Color.FromArgb(255, 82, 222, 239), Math.Max(1f, s * 0.021f))
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
    };
    var inner = s * 0.145f;
    var outerTick = s * 0.225f;
    graphics.DrawLine(tickPen, cx, cy - outerTick, cx, cy - inner);
    graphics.DrawLine(tickPen, cx, cy + inner, cx, cy + outerTick);
    graphics.DrawLine(tickPen, cx - outerTick, cy, cx - inner, cy);
    graphics.DrawLine(tickPen, cx + inner, cy, cx + outerTick, cy);

    var needle = new[]
    {
        new PointF(cx, cy - s * 0.215f),
        new PointF(cx + s * 0.105f, cy + s * 0.15f),
        new PointF(cx, cy + s * 0.09f),
        new PointF(cx - s * 0.105f, cy + s * 0.15f),
    };
    using var needleBrush = new LinearGradientBrush(
        new PointF(cx, cy - s * 0.215f),
        new PointF(cx, cy + s * 0.15f),
        Color.FromArgb(255, 168, 251, 255),
        Color.FromArgb(255, 22, 136, 165));
    using var needlePen = new Pen(Color.FromArgb(255, 234, 254, 255), Math.Max(1f, s * 0.017f));
    graphics.FillPolygon(needleBrush, needle);
    graphics.DrawPolygon(needlePen, needle);

    if (size >= 24)
    {
        var teamY = s * 0.745f;
        var teamRadius = s * 0.039f;
        var xs = new[] { s * 0.39f, s * 0.5f, s * 0.61f };
        var colors = new[]
        {
            Color.FromArgb(255, 87, 223, 241),
            Color.FromArgb(255, 154, 231, 125),
            Color.FromArgb(255, 210, 140, 255),
        };
        using var dotPen = new Pen(Color.FromArgb(255, 5, 12, 15), Math.Max(1f, s * 0.012f));
        for (var i = 0; i < xs.Length; i++)
        {
            using var dotBrush = new SolidBrush(colors[i]);
            graphics.FillEllipse(dotBrush, xs[i] - teamRadius, teamY - teamRadius, teamRadius * 2, teamRadius * 2);
            graphics.DrawEllipse(dotPen, xs[i] - teamRadius, teamY - teamRadius, teamRadius * 2, teamRadius * 2);
        }
    }

    using var memory = new MemoryStream();
    bitmap.Save(memory, ImageFormat.Png);
    return memory.ToArray();
}

static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
{
    var path = new GraphicsPath();
    var d = radius * 2f;
    path.AddArc(x, y, d, d, 180, 90);
    path.AddArc(x + width - d, y, d, d, 270, 90);
    path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
    path.AddArc(x, y + height - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}
