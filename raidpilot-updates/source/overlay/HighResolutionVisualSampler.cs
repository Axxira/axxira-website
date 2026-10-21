using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using GdiCaptureMethods = RaidPilot.Overlay.VisualMotionGate.GdiCaptureMethods;

namespace RaidPilot.Overlay;

/// <summary>
/// Capture haute résolution à la demande pour la vérification de lieux.
/// Elle reprend exactement le même recadrage central que VisualMotionGate,
/// mais à 160x90. Aucune image complète n'est conservée.
/// </summary>
internal static class HighResolutionVisualSampler
{
    internal const int SampleWidth = 160;
    internal const int SampleHeight = 90;

    private static readonly VisualMotionGateSettings CropSettings = new();

    internal static bool TryCreateLuminanceFrameFromImage(
        string path,
        out byte[] luminance)
    {
        luminance = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var source = Image.FromStream(
                stream,
                useEmbeddedColorManagement: false,
                validateImageData: false);
            if (source.Width < SampleWidth || source.Height < SampleHeight)
            {
                return false;
            }

            var region = VisualMotionGate.CalculateCaptureRegion(
                source.Width,
                source.Height,
                CropSettings);
            using var scaled = new Bitmap(
                SampleWidth,
                SampleHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, SampleWidth, SampleHeight),
                    new Rectangle(region.Left, region.Top, region.Width, region.Height),
                    GraphicsUnit.Pixel);
            }

            var result = new byte[SampleWidth * SampleHeight];
            for (var y = 0; y < SampleHeight; y++)
            {
                for (var x = 0; x < SampleWidth; x++)
                {
                    var color = scaled.GetPixel(x, y);
                    result[(y * SampleWidth) + x] = (byte)(
                        (color.R * 54 + color.G * 183 + color.B * 19 + 128) >> 8);
                }
            }

            luminance = result;
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            ExternalException or
            OutOfMemoryException)
        {
            return false;
        }
    }

    internal static async Task<byte[]?> CreateLuminanceFrameFromImageWithRetryAsync(
        string path,
        CancellationToken cancellationToken,
        int maximumAttempts = 5,
        TimeSpan? initialRetryDelay = null)
    {
        if (maximumAttempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        var retryDelay = initialRetryDelay ?? TimeSpan.FromMilliseconds(60);
        if (retryDelay < TimeSpan.Zero || retryDelay > TimeSpan.FromSeconds(2))
        {
            throw new ArgumentOutOfRangeException(nameof(initialRetryDelay));
        }

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await Task.Run(
                () => TryCreateLuminanceFrameFromImage(path, out var luminance)
                    ? luminance
                    : null,
                cancellationToken);
            if (frame is not null)
            {
                return frame;
            }

            if (attempt + 1 < maximumAttempts)
            {
                await Task.Delay(retryDelay * (attempt + 1), cancellationToken);
            }
        }

        return null;
    }

    internal static bool TryCaptureWindow(nint window, out byte[] luminance)
    {
        luminance = Array.Empty<byte>();
        if (window == nint.Zero
            || !GdiCaptureMethods.IsWindow(window)
            || GdiCaptureMethods.IsIconic(window)
            || !GdiCaptureMethods.GetClientRect(window, out var clientRect))
        {
            return false;
        }

        var clientWidth = clientRect.Right - clientRect.Left;
        var clientHeight = clientRect.Bottom - clientRect.Top;
        if (clientWidth < SampleWidth || clientHeight < SampleHeight)
        {
            return false;
        }

        var memoryDc = nint.Zero;
        var dib = nint.Zero;
        var previousObject = nint.Zero;
        var sourceDc = nint.Zero;
        try
        {
            memoryDc = GdiCaptureMethods.CreateCompatibleDC(nint.Zero);
            if (memoryDc == nint.Zero)
            {
                return false;
            }

            var bitmapInfo = new GdiCaptureMethods.BitmapInfo
            {
                Header = new GdiCaptureMethods.BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<GdiCaptureMethods.BitmapInfoHeader>(),
                    Width = SampleWidth,
                    Height = -SampleHeight,
                    Planes = 1,
                    BitCount = 32,
                    Compression = GdiCaptureMethods.BiRgb,
                },
            };
            dib = GdiCaptureMethods.CreateDIBSection(
                memoryDc,
                in bitmapInfo,
                GdiCaptureMethods.DibRgbColors,
                out var pixels,
                nint.Zero,
                0);
            if (dib == nint.Zero || pixels == nint.Zero)
            {
                return false;
            }

            previousObject = GdiCaptureMethods.SelectObject(memoryDc, dib);
            if (previousObject == nint.Zero
                || previousObject == GdiCaptureMethods.HgdiError)
            {
                return false;
            }

            GdiCaptureMethods.SetStretchBltMode(
                memoryDc,
                GdiCaptureMethods.ColorOnColor);

            sourceDc = GdiCaptureMethods.GetDC(window);
            if (sourceDc == nint.Zero)
            {
                return false;
            }

            var region = VisualMotionGate.CalculateCaptureRegion(
                clientWidth,
                clientHeight,
                CropSettings);
            if (!GdiCaptureMethods.StretchBlt(
                    memoryDc,
                    0,
                    0,
                    SampleWidth,
                    SampleHeight,
                    sourceDc,
                    region.Left,
                    region.Top,
                    region.Width,
                    region.Height,
                    GdiCaptureMethods.SourceCopy))
            {
                return false;
            }

            GdiCaptureMethods.GdiFlush();
            var bgra = new byte[SampleWidth * SampleHeight * 4];
            Marshal.Copy(pixels, bgra, 0, bgra.Length);
            var result = new byte[SampleWidth * SampleHeight];
            for (var pixel = 0; pixel < result.Length; pixel++)
            {
                var offset = pixel * 4;
                var blue = bgra[offset];
                var green = bgra[offset + 1];
                var red = bgra[offset + 2];
                result[pixel] = (byte)(
                    (red * 54 + green * 183 + blue * 19 + 128) >> 8);
            }

            luminance = result;
            return true;
        }
        finally
        {
            if (sourceDc != nint.Zero)
            {
                GdiCaptureMethods.ReleaseDC(window, sourceDc);
            }
            if (memoryDc != nint.Zero
                && previousObject != nint.Zero
                && previousObject != GdiCaptureMethods.HgdiError)
            {
                GdiCaptureMethods.SelectObject(memoryDc, previousObject);
            }
            if (dib != nint.Zero)
            {
                GdiCaptureMethods.DeleteObject(dib);
            }
            if (memoryDc != nint.Zero)
            {
                GdiCaptureMethods.DeleteDC(memoryDc);
            }
        }
    }
}