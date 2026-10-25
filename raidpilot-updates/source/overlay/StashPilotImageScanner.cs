using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace RaidPilot.Overlay;

/// <summary>
/// User-selected screenshot OCR for inventory, quests and hideout review.
/// No unattended game capture, item identification or confirmed inventory
/// entries. Every line is returned as an unverified candidate.
/// </summary>
internal sealed class StashPilotImageScanner
{
    internal static async Task<IReadOnlyList<string>> ScanAsync(
        string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Fichier manquant.");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > 24 * 1024 * 1024
            || !string.Equals(file.Extension, ".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choisir une image PNG valide (maximum 24 Mo).");
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) throw new NotSupportedException(
            "OCR Windows indisponible sur ce PC.");
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
        using var ras = stream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        if (decoder.PixelWidth is < 320 or > 8192
            || decoder.PixelHeight is < 200 or > 8192)
            throw new ArgumentException("Dimensions du screenshot non prises en charge.");

        var scale = Math.Min(1.0,
            OcrEngine.MaxImageDimension / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)Math.Floor(decoder.PixelWidth * scale)),
            ScaledHeight = Math.Max(1, (uint)Math.Floor(decoder.PixelHeight * scale))
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
        return FilterCandidates(result.Lines.Select(line => line.Text ?? ""));
    }

    internal static IReadOnlyList<string> FilterCandidates(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(line => (line ?? "").Trim())
            .Where(line => line.Length is >= 2 and <= 180
                && !line.Any(char.IsControl))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(160).ToArray();
    }
}
