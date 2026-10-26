using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace RaidPilot.Overlay;

/// <summary>
/// On-demand local icon references for visual matching. Never bundles images
/// taken from community wikis. Each image stays associated with a Tarkov item ID.
/// Downloads are bounded and only accepted from HTTPS asset hosts.
/// </summary>
internal static class StashPilotIconIndex
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidPilot", "StashPilot", "icons");

    internal sealed record Reference(string Id, string Name, string Path, ulong Hash);
    internal sealed record Candidate(string Id, string Name, int Distance);

    internal static async Task<Reference?> GetAsync(
        StashPilotItemCatalog.Item item, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(item.IconLink, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("assets.tarkov.dev", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("tarkov.dev", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".tarkov.dev", StringComparison.OrdinalIgnoreCase)))
            return null;

        var safeId = string.Concat(item.Id.Where(c => char.IsAsciiLetterOrDigit(c)));
        if (safeId.Length < 8 || safeId.Length > 80) return null;
        var file = Path.Combine(Root, safeId + ".png");
        try
        {
            if (!File.Exists(file))
            {
                using var response = await Client.GetAsync(uri,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 512_000)
                    return null;
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length is < 64 or > 512_000) return null;
                // Ensure PNG signature, never trust a remote file extension.
                if (!bytes.AsSpan(0, 8).SequenceEqual(
                    new byte[] {137,80,78,71,13,10,26,10})) return null;
                Directory.CreateDirectory(Root);
                var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
                try { File.Move(temp, file, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            using var bitmap = new Bitmap(file);
            if (bitmap.Width is < 8 or > 1024 || bitmap.Height is < 8 or > 1024)
                return null;
            return new Reference(item.Id, item.Name, file, AverageHash(bitmap));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
            or UnauthorizedAccessException or ArgumentException
            or System.Runtime.InteropServices.ExternalException) { return null; }
    }

    internal static ulong AverageHash(Bitmap bitmap)
    {
        using var scaled = new Bitmap(bitmap, new Size(8, 8));
        var lum = new int[64];
        var sum = 0;
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            var c = scaled.GetPixel(x, y);
            var value = (299 * c.R + 587 * c.G + 114 * c.B) / 1000;
            lum[y * 8 + x] = value;
            sum += value;
        }
        ulong hash = 0;
        for (var i = 0; i < 64; i++)
            if (lum[i] * 64 >= sum) hash |= 1UL << i;
        return hash;
    }

    internal static IReadOnlyList<Candidate> FindCandidates(
        Bitmap crop, IReadOnlyList<Reference> references)
    {
        var hash = AverageHash(crop);
        return references.Select(r => new Candidate(r.Id, r.Name,
                System.Numerics.BitOperations.PopCount(hash ^ r.Hash)))
            .OrderBy(r => r.Distance).ThenBy(r => r.Id, StringComparer.Ordinal)
            .Take(5).ToArray();
    }
}
