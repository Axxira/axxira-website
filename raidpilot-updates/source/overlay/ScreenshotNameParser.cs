using System.Globalization;
using System.Text.RegularExpressions;

namespace RaidPilot.Overlay;

public static partial class ScreenshotNameParser
{
    private const string Number = @"[-+]?(?:\d+(?:\.\d+)?|\.\d+)";

    [GeneratedRegex(
        @"^\d{4}-\d{2}-\d{2}\[\d{2}-\d{2}\]_?" +
        @"(?<x>" + Number + @"),\s*(?<y>" + Number + @"),\s*(?<z>" + Number + @")_?" +
        @"(?<qx>" + Number + @"),\s*(?<qy>" + Number + @"),\s*(?<qz>" + Number + @"),\s*(?<qw>" + Number + @")" +
        @"(?:_[^\\/:*?\""<>|]*)?\.png$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ScreenshotPattern();

    public static bool TryParse(string? fileName, out ScreenshotObservation? observation)
    {
        observation = null;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal))
        {
            return false;
        }

        var match = ScreenshotPattern().Match(safeName);
        if (!match.Success
            || !TryRead(match, "x", out var x)
            || !TryRead(match, "y", out var y)
            || !TryRead(match, "z", out var z)
            || !TryRead(match, "qx", out var qx)
            || !TryRead(match, "qy", out var qy)
            || !TryRead(match, "qz", out var qz)
            || !TryRead(match, "qw", out var qw))
        {
            return false;
        }

        var magnitude = Math.Sqrt((qx * qx) + (qy * qy) + (qz * qz) + (qw * qw));
        if (magnitude < 0.000001 || !double.IsFinite(magnitude))
        {
            return false;
        }

        qx /= magnitude;
        qy /= magnitude;
        qz /= magnitude;
        qw /= magnitude;

        // Project the Unity quaternion's forward (+Z) vector onto the X/Z
        // plane. This keeps the compass yaw stable while looking up or down.
        var sinYaw = 2.0 * ((qw * qy) + (qx * qz));
        var cosYaw = 1.0 - (2.0 * ((qx * qx) + (qy * qy)));
        var yaw = Math.Atan2(sinYaw, cosYaw) * (180.0 / Math.PI);

        observation = new ScreenshotObservation(safeName, x, y, z, qx, qy, qz, qw, yaw);
        return true;
    }

    private static bool TryRead(Match match, string groupName, out double value)
    {
        return double.TryParse(
            match.Groups[groupName].Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value)
            && double.IsFinite(value);
    }
}
