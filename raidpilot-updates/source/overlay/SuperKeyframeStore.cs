using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RaidPilot.Overlay;

internal static class SuperKeyframeStore
{
    private const int MaximumDatabaseBytes = 12 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        MaxDepth = 16,
    };

    internal static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidPilot Suite",
        "Localization");

    internal static string GetPath(string memberId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(memberId.Trim()));
        var fileId = Convert.ToHexString(hash.AsSpan(0, 16));
        return Path.Combine(
            RootDirectory,
            $"super-keyframes-{fileId}.json");
    }

    internal static bool TryLoad(
        SuperKeyframeRecognizer recognizer,
        string path)
    {
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists
                || file.Length is <= 0 or > MaximumDatabaseBytes)
            {
                return false;
            }

            var bytes = File.ReadAllBytes(file.FullName);
            if (bytes.Length is <= 0 or > MaximumDatabaseBytes)
            {
                return false;
            }

            var snapshot =
                JsonSerializer.Deserialize<SuperKeyframeDatabaseSnapshot>(
                    bytes,
                    JsonOptions);
            return recognizer.TryImportSnapshot(snapshot);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException or
            ArgumentException)
        {
            return false;
        }
    }

    internal static bool TrySave(
        SuperKeyframeRecognizer recognizer,
        string path)
    {
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? temporary = null;
        try
        {
            var snapshot = recognizer.CreateSnapshot();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                snapshot,
                JsonOptions);
            if (bytes.Length is <= 0 or > MaximumDatabaseBytes)
            {
                return false;
            }

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            temporary = fullPath
                + "."
                + Guid.NewGuid().ToString("N")
                + ".tmp";
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                32 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, fullPath, overwrite: true);
            temporary = null;
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException or
            ArgumentException)
        {
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch
                {
                }
            }
        }
    }
}
