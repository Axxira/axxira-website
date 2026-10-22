using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RaidPilot.Overlay;

internal sealed record CollectiveAtlasCandidate
{
    public int Protocol { get; init; } = CollectiveAtlasProtocol.ProtocolVersion;
    public string CandidateId { get; init; } = string.Empty;
    public string ContributorId { get; init; } = string.Empty;
    public string MapId { get; init; } = string.Empty;
    public long CreatedAtUnixMs { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double YawDegrees { get; init; }
    public string ClientVersion { get; init; } = string.Empty;
    public VisualKeyframeEntrySnapshot? Visual { get; init; }
    public SuperKeyframeEntrySnapshot? Super { get; init; }
}

internal static class CollectiveAtlasProtocol
{
    internal const int ProtocolVersion = 1;
    internal const int MinimumConsensusContributors = 3;
    internal const int MaximumCandidateJsonBytes = 96 * 1024;
    internal const double ClusterHorizontalMeters = 3.0;
    internal const double ClusterVerticalMeters = 4.0;
    internal const double ClusterYawDegrees = 18.0;

    internal static CollectiveAtlasCandidate CreateCandidate(
        string contributorId,
        string mapId,
        VisualKeyframePose pose,
        VisualKeyframeEntrySnapshot? visual,
        SuperKeyframeEntrySnapshot? super,
        long createdAtUnixMs,
        string clientVersion)
    {
        var candidate = new CollectiveAtlasCandidate
        {
            ContributorId = NormalizeContributorId(contributorId),
            MapId = NormalizeMapId(mapId),
            CreatedAtUnixMs = createdAtUnixMs,
            X = pose.X,
            Y = pose.Y,
            Z = pose.Z,
            YawDegrees = Normalize360(pose.YawDegrees),
            ClientVersion = NormalizeClientVersion(clientVersion),
            Visual = visual,
            Super = super,
        };
        return candidate with
        {
            CandidateId = ComputeCandidateId(candidate),
        };
    }

    internal static bool TryValidate(
        CollectiveAtlasCandidate? candidate,
        out CollectiveAtlasCandidate normalized)
    {
        normalized = default!;
        if (candidate is null
            || candidate.Protocol != ProtocolVersion
            || candidate.Visual is null && candidate.Super is null
            || !IsValidContributorId(candidate.ContributorId)
            || string.IsNullOrWhiteSpace(candidate.MapId)
            || candidate.MapId.Length > 64
            || candidate.CreatedAtUnixMs <= 0
            || !new[] { candidate.X, candidate.Y, candidate.Z, candidate.YawDegrees }.All(double.IsFinite)
            || Math.Abs(candidate.X) > 1_000_000
            || Math.Abs(candidate.Y) > 1_000_000
            || Math.Abs(candidate.Z) > 1_000_000)
        {
            return false;
        }

        var map = NormalizeMapId(candidate.MapId);
        if (map.Length == 0)
        {
            return false;
        }

        if (candidate.Visual is { } low
            && (!EntryPoseMatches(candidate, low.X, low.Y, low.Z, low.YawDegrees)
                || !ValidateVisualEntry(low)))
        {
            return false;
        }

        if (candidate.Super is { } high
            && (!EntryPoseMatches(candidate, high.X, high.Y, high.Z, high.YawDegrees)
                || !ValidateSuperEntry(high)))
        {
            return false;
        }

        var fixedCandidate = candidate with
        {
            ContributorId = NormalizeContributorId(candidate.ContributorId),
            MapId = map,
            YawDegrees = Normalize360(candidate.YawDegrees),
            ClientVersion = NormalizeClientVersion(candidate.ClientVersion),
        };
        var expectedId = ComputeCandidateId(fixedCandidate);
        if (!string.Equals(
                candidate.CandidateId,
                expectedId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = fixedCandidate with { CandidateId = expectedId };
        return true;
    }

    internal static bool ClusterCompatible(
        CollectiveAtlasCandidate left,
        CollectiveAtlasCandidate right)
    {
        if (!string.Equals(left.MapId, right.MapId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var horizontal = Math.Sqrt(
            Math.Pow(left.X - right.X, 2)
            + Math.Pow(left.Z - right.Z, 2));
        return horizontal <= ClusterHorizontalMeters
            && Math.Abs(left.Y - right.Y) <= ClusterVerticalMeters
            && AngleDistance(left.YawDegrees, right.YawDegrees) <= ClusterYawDegrees;
    }

    internal static bool PoseCompatible(
        CollectiveAtlasCandidate candidate,
        string mapId,
        VisualKeyframePose pose)
    {
        var probe = new CollectiveAtlasCandidate
        {
            MapId = NormalizeMapId(mapId),
            X = pose.X,
            Y = pose.Y,
            Z = pose.Z,
            YawDegrees = Normalize360(pose.YawDegrees),
        };
        return ClusterCompatible(candidate, probe);
    }

    internal static string ComputeCandidateId(CollectiveAtlasCandidate candidate)
    {
        var builder = new StringBuilder(8192);
        builder.Append(ProtocolVersion).Append('|')
            .Append(NormalizeContributorId(candidate.ContributorId)).Append('|')
            .Append(NormalizeMapId(candidate.MapId)).Append('|')
            .Append(candidate.X.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(candidate.Y.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(candidate.Z.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(Normalize360(candidate.YawDegrees).ToString("R", CultureInfo.InvariantCulture)).Append('|');

        if (candidate.Visual is { } low)
        {
            builder.Append("L|")
                .Append(low.BitsBase64).Append('|')
                .Append(low.ValidBitsBase64).Append('|')
                .Append(low.ValidBitCount).Append('|')
                .Append(low.TextureStandardDeviation.ToString("R", CultureInfo.InvariantCulture));
        }
        if (candidate.Super is { } high)
        {
            builder.Append("|H|")
                .Append(high.GlobalBase64).Append('|')
                .Append(high.GlobalValidBase64).Append('|')
                .Append(high.GradientBase64).Append('|')
                .Append(high.CensusBitsBase64).Append('|')
                .Append(high.CensusValidBase64).Append('|')
                .Append(high.RegionHashesBase64).Append('|')
                .Append(high.RegionValidBase64).Append('|')
                .Append(high.ValidBitCount).Append('|')
                .Append(high.TextureStandardDeviation.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(high.GradientEnergy.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(high.MeanLuminance.ToString("R", CultureInfo.InvariantCulture));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash.AsSpan(0, 24));
    }

    internal static string NormalizeMapId(string? value) =>
        new string((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .Take(64)
            .ToArray());

    internal static double Normalize360(double value)
    {
        var normalized = value % 360.0;
        if (normalized < 0)
        {
            normalized += 360.0;
        }
        return normalized;
    }

    internal static double AngleDistance(double a, double b)
    {
        var delta = Math.Abs(Normalize360(a) - Normalize360(b));
        return Math.Min(delta, 360.0 - delta);
    }

    internal static string NormalizeContributorId(string? value) =>
        new string((value ?? string.Empty)
            .Trim()
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .Take(64)
            .ToArray());

    private static bool IsValidContributorId(string value)
    {
        var normalized = NormalizeContributorId(value);
        return normalized.Length is >= 16 and <= 64
            && string.Equals(normalized, value.Trim(), StringComparison.Ordinal);
    }

    private static string NormalizeClientVersion(string? value) =>
        new string((value ?? string.Empty)
            .Where(c => !char.IsControl(c))
            .Take(32)
            .ToArray())
            .Trim();

    private static bool EntryPoseMatches(
        CollectiveAtlasCandidate candidate,
        double x,
        double y,
        double z,
        double yaw)
    {
        if (!new[] { x, y, z, yaw }.All(double.IsFinite))
        {
            return false;
        }

        var distance = Math.Sqrt(
            Math.Pow(candidate.X - x, 2)
            + Math.Pow(candidate.Z - z, 2));
        return distance <= 0.75
            && Math.Abs(candidate.Y - y) <= 1.0
            && AngleDistance(candidate.YawDegrees, yaw) <= 4.0;
    }

    private static bool ValidateVisualEntry(VisualKeyframeEntrySnapshot entry) =>
        entry.ValidBitCount > 0
        && double.IsFinite(entry.TextureStandardDeviation)
        && entry.TextureStandardDeviation >= 0
        && IsValidBase64(entry.BitsBase64, 16 * 1024)
        && IsValidBase64(entry.ValidBitsBase64, 16 * 1024);

    private static bool ValidateSuperEntry(SuperKeyframeEntrySnapshot entry) =>
        entry.ValidBitCount > 0
        && new[]
        {
            entry.TextureStandardDeviation,
            entry.GradientEnergy,
            entry.MeanLuminance,
        }.All(double.IsFinite)
        && IsValidBase64(entry.GlobalBase64, 16 * 1024)
        && IsValidBase64(entry.GlobalValidBase64, 16 * 1024)
        && IsValidBase64(entry.GradientBase64, 32 * 1024)
        && IsValidBase64(entry.CensusBitsBase64, 32 * 1024)
        && IsValidBase64(entry.CensusValidBase64, 32 * 1024)
        && IsValidBase64(entry.RegionHashesBase64, 16 * 1024)
        && IsValidBase64(entry.RegionValidBase64, 16 * 1024);

    private static bool IsValidBase64(string value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumBytes * 2)
        {
            return false;
        }

        try
        {
            var length = Convert.FromBase64String(value).Length;
            return length > 0 && length <= maximumBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
