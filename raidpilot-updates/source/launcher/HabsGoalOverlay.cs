using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RaidPilot.Launcher;

internal sealed class HabsGoalOverlay : Form
{
    private readonly Func<string?> _gamePath;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly Stopwatch _animation = new();
    private DateTimeOffset _previewUntil;
    private string _caption = "CANADIENS DE MONTRÉAL";
    private nint _checkedWindow;
    private string? _checkedPath;
    private DateTimeOffset _nextWindowCheck;
    private bool _isGame;
    private bool _previewRunning;
    private bool _celebrationRunning;
    internal event Action<string>? PreviewStatus;
    internal event Action<string>? GoalStatus;

    internal HabsGoalOverlay(Func<string?> gamePath)
    {
        _gamePath = gamePath;
        Text = "ForeverPilot — Lumières de but";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        _timer.Tick += (_, _) => TickFrame();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            // Per-pixel alpha + mouse pass-through + no taskbar + never activate.
            p.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000;
            return p;
        }
    }

    internal void ArmPreview()
    {
        Stop();
        _previewUntil = DateTimeOffset.UtcNow.AddSeconds(20);
        _timer.Start();
    }

    internal void Celebrate(string caption)
    {
        if (!TryGameBounds(out _))
        {
            GoalStatus?.Invoke("but reçu · WoW hors premier plan");
            return; // Consume goals while outside WoW, never replay on return.
        }
        _previewUntil = default;
        _previewRunning = false;
        _celebrationRunning = true;
        _caption = caption;
        _animation.Restart();
        _timer.Start();
        GoalStatus?.Invoke("but reçu · effet lancé dans WoW");
        TickFrame();
    }

    internal void Stop()
    {
        if (_celebrationRunning)
        {
            GoalStatus?.Invoke(_animation.Elapsed.TotalSeconds >= HabsGoalPresentation.DurationSeconds
                ? "but · effet terminé (10 s)" : "but · effet interrompu hors WoW");
            _celebrationRunning = false;
        }
        if (_previewRunning)
        {
            PreviewStatus?.Invoke(_animation.Elapsed.TotalSeconds >= HabsGoalPresentation.DurationSeconds
                ? "Aperçu terminé · 10 secondes · WoW seulement" : "Aperçu interrompu en quittant WoW");
            _previewRunning = false;
        }
        _timer.Stop();
        _animation.Reset();
        _previewUntil = default;
        Hide();
    }

    private void TickFrame()
    {
        var hasGame = TryGameBounds(out var bounds);
        if (_previewUntil != default)
        {
            if (DateTimeOffset.UtcNow >= _previewUntil)
            {
                PreviewStatus?.Invoke("Test expiré · Ouvre WoW puis relance le test");
                Stop(); return;
            }
            if (!hasGame) return;
            _caption = "APERÇU  ·  CANADIENS DE MONTRÉAL";
            _previewUntil = default;
            _animation.Restart();
            _previewRunning = true;
        }
        var elapsed = _animation.Elapsed.TotalSeconds;
        if (!hasGame || !HabsGoalPresentation.Visible(elapsed)) { Stop(); return; }
        try
        {
            using var frame = HabsGoalArt.Render(bounds.Width, elapsed, _caption);
            Present(frame, bounds.Location, HabsGoalPresentation.Opacity(elapsed));
            if (!Visible)
            {
                Show();
                if (_previewRunning) PreviewStatus?.Invoke("Aperçu en cours dans WoW · 10 secondes");
            }
            Native.SetWindowPos(Handle, new nint(-1), 0, 0, 0, 0, 0x13);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            var wasGoal = _celebrationRunning;
            Stop();
            if (wasGoal) GoalStatus?.Invoke("but reçu · erreur d'affichage " + e.NativeErrorCode);
            PreviewStatus?.Invoke("Affichage indisponible · Code " + e.NativeErrorCode);
        }
    }

    private bool TryGameBounds(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        var window = Native.GetForegroundWindow();
        var gamePath = _gamePath();
        if (window == 0 || string.IsNullOrWhiteSpace(gamePath) || Native.IsIconic(window)) return false;
        if (window != _checkedWindow || gamePath != _checkedPath || DateTimeOffset.UtcNow >= _nextWindowCheck)
        {
            _checkedWindow = window;
            _checkedPath = gamePath;
            _nextWindowCheck = DateTimeOffset.UtcNow.AddSeconds(1);
            _isGame = false;
            try
            {
                Native.GetWindowThreadProcessId(window, out uint pid);
                using var process = Process.GetProcessById((int)pid);
                _isGame = string.Equals(process.MainModule?.FileName, Path.Combine(gamePath, "Wow.exe"), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { }
        }
        if (!_isGame || !Native.GetClientRect(window, out var rect)) return false;
        var origin = new Point();
        if (!Native.ClientToScreen(window, ref origin) || rect.Right < 400 || rect.Bottom < 240) return false;
        bounds = new Rectangle(origin.X, origin.Y, rect.Right, rect.Bottom);
        return true;
    }

    private void Present(Bitmap bitmap, Point position, byte alpha)
    {
        var screen = Native.GetDC(0);
        var dc = Native.CreateCompatibleDC(screen);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var old = Native.SelectObject(dc, hBitmap);
        try
        {
            var size = bitmap.Size;
            var source = new Point();
            var blend = new Native.Blend { SourceConstantAlpha = alpha, AlphaFormat = 1 };
            if (!Native.UpdateLayeredWindow(Handle, screen, ref position, ref size, dc, ref source, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Native.SelectObject(dc, old);
            Native.DeleteObject(hBitmap);
            Native.DeleteDC(dc);
            Native.ReleaseDC(0, screen);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Stop(); _timer.Dispose(); }
        base.Dispose(disposing);
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { internal byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(nint window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint window, ref Point point);
        [DllImport("user32.dll")] internal static extern nint GetDC(nint window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(nint window, nint dc);
        [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
        [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(nint window, nint screen,
            ref Point destination, ref Size size, nint sourceDc, ref Point source, int key, ref Blend blend, int flags);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int w, int h, int flags);
    }
}

internal static class HabsGoalArt
{
    internal static Bitmap Render(int width, double seconds, string caption)
    {
        float scale = Math.Clamp(width / 1920f, .6f, 1.5f);
        var bitmap = new Bitmap(width, (int)(200 * scale), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.ScaleTransform(scale, scale);
        float w = width / scale;
        float pulse = .5f + .5f * (float)Math.Sin(seconds * Math.PI * 1.6);
        float middle = w / 2;

        // A thin illuminated tricolour ribbon bridges the beacons; the playfield stays clear.
        var band = new RectangleF(86, 23, w - 172, 29);
        using (var fill = new LinearGradientBrush(band, Color.FromArgb(245, 9, 20, 43), Color.FromArgb(235, 39, 6, 17), 90))
            g.FillRectangle(fill, band);
        GlowLine(g, Color.FromArgb(210, 223, 25, 55), 100, 23, w - 100, 23, 2);
        GlowLine(g, Color.FromArgb(190, 40, 108, 244), 100, 52, w - 100, 52, 2);
        using (var white = new Pen(Color.FromArgb(140, 235, 244, 255), 1)) g.DrawLine(white, 95, 37, w - 95, 37);
        for (int x = 108; x < w - 100; x += 12)
        {
            var a = 50 + (int)(155 * (.5 + .5 * Math.Sin(x / 55.0 - seconds * 5)));
            using var dot = new SolidBrush(Color.FromArgb(a, 255, 66, 75));
            g.FillEllipse(dot, x, 28, 3, 3);
        }

        var plaque = new RectangleF(middle - 245, 12, 490, 130);
        using var outline = Round(plaque, 15);
        for (int i = 18; i >= 4; i -= 3)
        {
            using var glow = new Pen(Color.FromArgb(8 + (int)(pulse * 5), 255, 20, 56), i);
            g.DrawPath(glow, outline);
        }
        using (var bezel = new LinearGradientBrush(plaque, Color.FromArgb(255, 161, 177, 198), Color.FromArgb(255, 34, 42, 61), 90))
            g.FillPath(bezel, outline);
        using var inside = Round(new RectangleF(middle - 242, 15, 484, 124), 12);
        using (var face = new LinearGradientBrush(plaque, Color.FromArgb(255, 9, 19, 39), Color.FromArgb(255, 31, 4, 14), 90))
            g.FillPath(face, inside);
        using (var red = new Pen(Color.FromArgb(240, 255, 55, 76), 2)) g.DrawPath(red, inside);
        // Subtle LED mesh and a specular top edge give the sign depth.
        using (var mesh = new SolidBrush(Color.FromArgb(30, 224, 62, 87)))
            for (float x = middle - 228; x < middle + 228; x += 7)
            for (int y = 25; y < 130; y += 7) g.FillEllipse(mesh, x, y, 1.5f, 1.5f);
        Text(g, "C A N A D I E N S   D E   M O N T R É A L", middle, 20, 9, Color.FromArgb(235, 199, 215, 239));
        DrawLedWords(g, middle, 43, pulse);
        Text(g, caption, middle, 115, 10, Color.FromArgb(245, 220, 230, 248));
        GlowLine(g, Color.FromArgb(210, 70, 143, 255), middle - 181, 105, middle + 181, 105, 1.5f);
        DrawBeacon(g, 65, 66, seconds, 0);
        DrawBeacon(g, w - 65, 66, seconds, Math.PI);
        return bitmap;
    }

    private static void DrawBeacon(Graphics g, float cx, float cy, double seconds, double phase)
    {
        float turn = (float)Math.Sin(seconds * 5 + phase);
        using (var haloShape = new GraphicsPath())
        {
            haloShape.AddEllipse(cx - 85, cy - 73, 170, 146);
            using var glow = new PathGradientBrush(haloShape)
            {
                CenterColor = Color.FromArgb(140 + (int)(40 * Math.Abs(turn)), 255, 12, 40),
                SurroundColors = [Color.FromArgb(0, 255, 12, 40)],
                FocusScales = new PointF(.25f, .25f),
            };
            g.FillPath(glow, haloShape);
        }
        var glass = new RectangleF(cx - 37, cy - 44, 74, 76);
        using var dome = new GraphicsPath();
        dome.AddArc(glass.X, glass.Y, glass.Width, 61, 180, 180);
        dome.AddLine(glass.Right, cy - 13, glass.Right, cy + 27);
        dome.AddBezier(glass.Right, cy + 27, cx + 20, cy + 38, cx - 20, cy + 38, glass.Left, cy + 27);
        dome.CloseFigure();
        using (var lens = new LinearGradientBrush(glass, Color.FromArgb(255, 109, 3, 21), Color.FromArgb(255, 249, 27, 44), 0f))
        {
            lens.InterpolationColors = new ColorBlend { Colors = [Color.FromArgb(255, 73, 2, 13), Color.FromArgb(255, 242, 19, 39),
                Color.FromArgb(255, 255, 80, 82), Color.FromArgb(255, 141, 5, 26)], Positions = [0, .35f, .58f, 1] };
            g.FillPath(lens, dome);
        }
        var saved = g.Save();
        g.SetClip(dome);
        using (var core = new LinearGradientBrush(new RectangleF(cx - 18 + turn * 23, cy - 42, 24, 81),
            Color.FromArgb(0, 255, 238, 194), Color.FromArgb(245, 255, 239, 204), 0f))
        {
            core.InterpolationColors = new ColorBlend { Colors = [Color.FromArgb(0, 255, 220, 190), Color.FromArgb(245, 255, 233, 198), Color.FromArgb(0, 255, 220, 190)], Positions = [0, .5f, 1] };
            g.FillRectangle(core, cx - 18 + turn * 23, cy - 42, 24, 80);
        }
        using (var ribs = new Pen(Color.FromArgb(95, 87, 0, 16), 1.5f))
            for (int y = -16; y <= 26; y += 7) g.DrawArc(ribs, cx - 39, cy + y - 6, 78, 16, 0, 180);
        using (var reflection = new Pen(Color.FromArgb(180, 255, 202, 198), 3))
            g.DrawArc(reflection, cx - 30, cy - 38, 60, 44, 195, 76);
        g.Restore(saved);
        using (var rim = new Pen(Color.FromArgb(240, 255, 116, 127), 1.4f)) g.DrawPath(rim, dome);
        var foot = new RectangleF(cx - 43, cy + 29, 86, 23);
        using var baseShape = Round(foot, 6);
        using (var metal = new LinearGradientBrush(foot, Color.FromArgb(255, 180, 192, 206), Color.FromArgb(255, 19, 23, 34), 90))
            g.FillPath(metal, baseShape);
        using (var black = new SolidBrush(Color.FromArgb(255, 9, 13, 22))) g.FillRectangle(black, cx - 40, cy + 44, 80, 3);
        using (var screws = new SolidBrush(Color.FromArgb(255, 205, 217, 231)))
        { g.FillEllipse(screws, cx - 32, cy + 35, 3, 3); g.FillEllipse(screws, cx + 29, cy + 35, 3, 3); }
        GlowLine(g, Color.FromArgb(195, 255, 31, 58), cx - 33, cy + 50, cx + 33, cy + 50, 1);
    }

    private static readonly string[] Led = ["10000/10000/10000/10000/10000/10000/11111", "11111/10000/10000/11110/10000/10000/11111",
        "000/000/000/000/000/000/000", "11110/10001/10001/11110/10001/10001/11110", "10001/10001/10001/10001/10001/10001/01110", "11111/00100/00100/00100/00100/00100/00100"];
    private static void DrawLedWords(Graphics g, float center, float top, float pulse)
    {
        const float spacing = 8.5f;
        float x = center - 33 * spacing / 2;
        foreach (var letter in Led)
        {
            var rows = letter.Split('/');
            for (int y = 0; y < 7; y++)
            for (int c = 0; c < rows[y].Length; c++)
            {
                if (rows[y][c] != '1') continue;
                float px = x + c * spacing, py = top + y * spacing;
                using var halo = new SolidBrush(Color.FromArgb(50 + (int)(pulse * 25), 255, 52, 69));
                using var bulb = new SolidBrush(Color.FromArgb(255, 255, 231, 224));
                g.FillEllipse(halo, px - 2, py - 2, 10, 10);
                g.FillEllipse(bulb, px, py, 6, 6);
            }
            x += (rows[0].Length + 1) * spacing;
        }
    }

    private static void GlowLine(Graphics g, Color color, float x1, float y1, float x2, float y2, float width)
    {
        for (int i = 3; i >= 1; i--)
        {
            using var halo = new Pen(Color.FromArgb(18, color), width + i * 4);
            g.DrawLine(halo, x1, y1, x2, y2);
        }
        using var core = new Pen(color, width);
        g.DrawLine(core, x1, y1, x2, y2);
    }
    private static void Text(Graphics g, string value, float center, float y, float size, Color color)
    {
        using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(value, font, brush, new PointF(center, y), format);
    }
    private static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}
