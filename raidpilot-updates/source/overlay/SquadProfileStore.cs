using System.Text.Json;

namespace RaidPilot.Overlay;

public sealed record SquadProfile(
    string MemberId,
    string Name,
    string? RoomCode,
    bool AutoJoin)
{
    public bool HasRoom => SquadRoomCode.TryNormalize(RoomCode, out _);
}

public static class SquadProfileStore
{
    private const int SchemaVersion = 1;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaidPilot",
        "squad-profile.json");

    public static SquadProfile LoadOrCreate(string? path = null)
    {
        var target = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        try
        {
            if (File.Exists(target))
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(target));
                var root = document.RootElement;
                if (root.TryGetProperty("schemaVersion", out var schema)
                    && schema.TryGetInt32(out var schemaValue)
                    && schemaValue == SchemaVersion
                    && root.TryGetProperty("memberId", out var member)
                    && member.ValueKind == JsonValueKind.String
                    && root.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String)
                {
                    var memberId = member.GetString();
                    var displayName = name.GetString();
                    var roomCode = root.TryGetProperty("roomCode", out var room)
                        && room.ValueKind == JsonValueKind.String
                        ? room.GetString()
                        : null;
                    var autoJoin = root.TryGetProperty("autoJoin", out var join)
                        && join.ValueKind is JsonValueKind.True or JsonValueKind.False
                        && join.GetBoolean();

                    if (!string.IsNullOrWhiteSpace(memberId)
                        && memberId.Length <= 64)
                    {
                        return new SquadProfile(
                            memberId,
                            SquadRelayClient.NormalizeName(displayName),
                            SquadRoomCode.TryNormalize(roomCode, out var normalized)
                                ? SquadRoomCode.Format(normalized)
                                : null,
                            autoJoin);
                    }
                }
            }
        }
        catch
        {
        }

        var profile = new SquadProfile(
            Guid.NewGuid().ToString("N"),
            Environment.UserName.Length is > 0 and <= 24
                ? SquadRelayClient.NormalizeName(Environment.UserName)
                : "Joueur",
            null,
            false);
        _ = TrySave(profile, target);
        return profile;
    }

    public static bool TrySave(SquadProfile profile, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var target = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        string? temporary = null;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(target));
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }
            Directory.CreateDirectory(directory);

            var normalizedRoom = SquadRoomCode.TryNormalize(profile.RoomCode, out var normalized)
                ? SquadRoomCode.Format(normalized)
                : null;
            var json = JsonSerializer.Serialize(new
            {
                schemaVersion = SchemaVersion,
                memberId = profile.MemberId,
                name = SquadRelayClient.NormalizeName(profile.Name),
                roomCode = normalizedRoom,
                autoJoin = profile.AutoJoin && normalizedRoom is not null,
            }, new JsonSerializerOptions { WriteIndented = true });

            temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, target, overwrite: true);
            temporary = null;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); } catch { }
            }
        }
    }
}
