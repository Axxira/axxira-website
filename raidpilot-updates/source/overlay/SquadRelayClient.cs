using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RaidPilot.Overlay;

public sealed record SquadMemberPose(
    string MemberId,
    string Name,
    string Map,
    double X,
    double Y,
    double Z,
    double YawDegrees,
    double UncertaintyMeters,
    long SentAtUnixMilliseconds);

/// <summary>
/// Only public quest/objective identifiers and an opt-in squad nickname.
/// No player account, screenshots, logs, coordinates or secrets.
/// </summary>
public sealed record SquadQuestPlan(
    string PeerId,
    string Name,
    string Mode,
    IReadOnlyList<string> Quests,
    IReadOnlyList<string> Done,
    long SentAtUnixMilliseconds);

public sealed record SquadQuestPlanSnapshot(
    string Mode,
    IReadOnlyList<string> Quests,
    IReadOnlyList<string> Done);


public static class SquadRoomCode
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 16;

    public static string Generate()
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return Format(new string(chars));
    }

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var compact = new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (compact.StartsWith("RP", StringComparison.Ordinal) && compact.Length == CodeLength + 2)
        {
            compact = compact[2..];
        }

        if (compact.Length != CodeLength || compact.Any(c => !Alphabet.Contains(c)))
        {
            return false;
        }

        normalized = compact;
        return true;
    }

    public static string Format(string normalized)
    {
        if (!TryNormalize(normalized, out var compact))
        {
            throw new ArgumentException("Code d'escouade RaidPilot invalide.", nameof(normalized));
        }

        return $"RP-{compact[..4]}-{compact[4..8]}-{compact[8..12]}-{compact[12..16]}";
    }

    public static string ToSessionId(string roomCode)
    {
        if (!TryNormalize(roomCode, out var normalized))
        {
            throw new ArgumentException("Code d'escouade RaidPilot invalide.", nameof(roomCode));
        }
        return "RPSQ" + normalized;
    }
}

/// <summary>
/// Per-machine single-writer election. A held file handle is automatically
/// released by Windows after a process exits or crashes. Passive clients may
/// receive teammates but cannot duplicate their own outbound coordinates.
/// </summary>
internal sealed class SquadBroadcastLease : IDisposable
{
    private readonly string _path;
    private readonly object _gate = new();
    private FileStream? _handle;

    internal SquadBroadcastLease(string sessionId, string memberId)
    {
        var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(
            "raidpilot-broadcast-v1:" + sessionId + ":" + memberId));
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaidPilot", "squad-writer-" + Convert.ToHexString(fingerprint)[..32] + ".lock");
    }

    internal bool IsHeld
    {
        get { lock (_gate) return _handle is not null; }
    }

    internal bool TryAcquire()
    {
        lock (_gate)
        {
            if (_handle is not null) return true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                _handle = new FileStream(_path, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    internal void Release()
    {
        lock (_gate)
        {
            _handle?.Dispose();
            _handle = null;
        }
    }

    public void Dispose() => Release();
}

public sealed class SquadRelayClient : IAsyncDisposable
{
    private const int ProtocolVersion = 1;
    private const int MaximumInboundBytes = 32 * 1024;
    private static readonly Uri SocketBaseUri = new("wss://raidpilot-team-relay.tyronixstudio.workers.dev/ws");
    private static readonly Uri HealthUri = new("https://raidpilot-team-relay.tyronixstudio.workers.dev/health");
    private static readonly HttpClient CapabilityHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(3),
        MaxResponseContentBufferSize = 4096,
    };
    private static readonly System.Text.RegularExpressions.Regex TaskIdPattern =
        new("^[0-9a-fA-F]{24}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex StepIdPattern =
        new("^[0-9a-fA-F]{24}:[0-9a-fA-F]{24}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);


    private readonly string _roomCode;
    private readonly string _sessionId;
    private readonly string _memberId;
    private readonly string _name;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _senderGate = new(1, 1);
    private readonly SemaphoreSlim _receiverGate = new(1, 1);
    private readonly SquadBroadcastLease _broadcastLease;
    private ClientWebSocket? _receiver;
    private Task? _receiverLoop;
    private int _disposed;
    private int _questPlanSupported;

    public SquadRelayClient(string roomCode, string memberId, string name)
    {
        if (!SquadRoomCode.TryNormalize(roomCode, out var normalized))
        {
            throw new ArgumentException("Code d'escouade invalide.", nameof(roomCode));
        }
        if (string.IsNullOrWhiteSpace(memberId) || memberId.Length > 64)
        {
            throw new ArgumentException("Identifiant de membre invalide.", nameof(memberId));
        }

        _roomCode = SquadRoomCode.Format(normalized);
        _sessionId = DeriveRelayTeamKey(normalized);
        _memberId = memberId.Trim();
        _name = NormalizeName(name);
        _broadcastLease = new SquadBroadcastLease(_sessionId, _memberId);
    }

    public string RoomCode => _roomCode;
    public string MemberId => _memberId;
    public string Name => _name;
    public bool IsReceiving => _receiverLoop is { IsCompleted: false };
    public bool IsConnected => _receiver?.State == WebSocketState.Open;
    public bool IsPrimarySender => _broadcastLease.IsHeld;
    public bool QuestPlanSupported => Volatile.Read(ref _questPlanSupported) != 0;

    internal static string DeriveRelayTeamKey(string teamCode)
    {
        if (!SquadRoomCode.TryNormalize(teamCode, out var normalized))
            throw new ArgumentException("Code d'escouade invalide.", nameof(teamCode));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("raidpilot-cloudflare-relay-v1:" + normalized));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static Uri BuildRelayEndpoint(string teamCode, string name) =>
        new($"{SocketBaseUri}?team={Uri.EscapeDataString(DeriveRelayTeamKey(teamCode))}&name={Uri.EscapeDataString(NormalizeName(name))}");

    public event Action<SquadMemberPose>? PoseReceived;
    public event Action<SquadQuestPlan>? QuestPlanReceived;
    public event Action<string>? QuestPeerLeft;
    public event Action<bool>? ReceiverConnectionChanged;

    /// <summary>
    /// A trusted feature advertisement is required before any quest plan is
    /// sent. Old production Workers never advertised questplan: fail closed.
    /// </summary>
    public async Task<bool> ProbeQuestPlanSupportAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _questPlanSupported, 0);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await CapabilityHttp.GetAsync(
                HealthUri, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !ReadString(root, "service", out var service)
                || service != "RaidPilot Team Relay"
                || !root.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Object
                || !features.TryGetProperty("questplan", out var quest)
                || quest.ValueKind != JsonValueKind.True)
                return false;
            Interlocked.Exchange(ref _questPlanSupported, 1);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or OperationCanceledException
            or JsonException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<bool> SendQuestPlanAsync(
        SquadQuestPlanSnapshot plan, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!QuestPlanSupported || !ValidQuestPlan(plan)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        await StartReceivingAsync(timeout.Token).ConfigureAwait(false);
        await _senderGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var socket = await EnsureReceiverConnectedAsync(
                timeout.Token).ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(new
            {
                type = "questplan",
                mode = plan.Mode,
                quests = plan.Quests,
                done = plan.Done,
                at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            await SendTextAsync(socket, payload, timeout.Token).ConfigureAwait(false);
            return true;
        }
        finally { _senderGate.Release(); }
    }

    internal static bool ValidQuestPlan(SquadQuestPlanSnapshot? plan) =>
        plan is not null && (plan.Mode is "pve" or "regular" or "pvp-season")
        && plan.Quests is { Count: <= 80 } && plan.Done is { Count: <= 120 }
        && plan.Quests.All(id => id is not null && TaskIdPattern.IsMatch(id))
        && plan.Done.All(id => id is not null && StepIdPattern.IsMatch(id));


    public async Task StartReceivingAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _startGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_receiverLoop is { IsCompleted: false }) return;
            await EnsureReceiverConnectedAsync(linked.Token).ConfigureAwait(false);
            _receiverLoop = Task.Run(
                () => ReceiverReconnectLoopAsync(_lifetime.Token), CancellationToken.None);
        }
        finally { _startGate.Release(); }
    }

    public async Task<bool> SendPoseAsync(
        SquadMemberPose pose, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(pose);
        if (!string.Equals(pose.MemberId, _memberId, StringComparison.Ordinal)
            || !string.Equals(pose.Name, _name, StringComparison.Ordinal)
            || !IsValidPose(pose))
        {
            throw new ArgumentException("Pose d'escouade invalide.", nameof(pose));
        }

        // Beacon and Overlay use the same saved member profile. Only one
        // sender may hold the lease; a passive Overlay still keeps its
        // receiver open so teammates remain visible on the map.
        if (!_broadcastLease.TryAcquire()) return false;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            await StartReceivingAsync(timeout.Token).ConfigureAwait(false);
            await _senderGate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                var socket = await EnsureReceiverConnectedAsync(
                    timeout.Token).ConfigureAwait(false);
                var payload = JsonSerializer.Serialize(new
                {
                    type = "pose",
                    map = NormalizeMapKey(pose.Map),
                    position = new { x = pose.X, y = pose.Y, z = pose.Z },
                    rotation = Normalize360(pose.YawDegrees),
                    floor = (string?)null,
                });
                try
                {
                    await SendTextAsync(socket, payload, timeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    try { socket.Abort(); } catch { }
                    throw;
                }
                return true;
            }
            finally
            {
                _senderGate.Release();
            }
        }
        catch
        {
            // A network failure releases ownership so the other local
            // component can take over on its next 100 ms send interval.
            _broadcastLease.Release();
            throw;
        }
    }

    private async Task ReceiverReconnectLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? receiver = null;
            try
            {
                receiver = await EnsureReceiverConnectedAsync(cancellationToken).ConfigureAwait(false);
                ReceiverConnectionChanged?.Invoke(true);
                await ReceiveLoopAsync(receiver, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // The bounded reconnect below is authoritative.
            }
            finally
            {
                ReceiverConnectionChanged?.Invoke(false);
                await _receiverGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    if (ReferenceEquals(receiver, _receiver))
                    {
                        AbortAndDispose(ref _receiver);
                    }
                }
                finally
                {
                    _receiverGate.Release();
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<ClientWebSocket> EnsureReceiverConnectedAsync(CancellationToken cancellationToken)
    {
        await _receiverGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_receiver?.State == WebSocketState.Open)
            {
                return _receiver;
            }

            AbortAndDispose(ref _receiver);
            var socket = CreateSocket();
            try
            {
                await socket.ConnectAsync(
                    BuildEndpoint(_sessionId, _name),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
            _receiver = socket;
            return socket;
        }
        finally
        {
            _receiverGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested
            && socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                if (message.Length + result.Count > MaximumInboundBytes)
                {
                    socket.Abort();
                    return;
                }
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(message.ToArray());
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (!ReadString(root, "type", out var type)) continue;
                if (type == "pose") PublishIncoming(root);
                else if (type == "questplan" && QuestPlanSupported)
                    PublishIncomingQuestPlan(root);
                else if (type == "peer_left" && QuestPlanSupported
                    && ReadString(root, "id", out var leftId)
                    && leftId.Length <= 64)
                {
                    try { QuestPeerLeft?.Invoke(leftId); } catch { }
                }
                else if (type == "welcome"
                    && root.TryGetProperty("peers", out var peers)
                    && peers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var peer in peers.EnumerateArray()) PublishIncoming(peer);
                }
            }
        }
    }

    private void PublishIncoming(JsonElement data)
    {
        if (TryReadPose(data, out var pose) && pose is not null
            && !string.Equals(pose.MemberId, _memberId, StringComparison.Ordinal))
        {
            try { PoseReceived?.Invoke(pose); }
            catch { /* UI callbacks cannot disrupt the receiver. */ }
        }
    }

    private void PublishIncomingQuestPlan(JsonElement root)
    {
        if (TryReadQuestPlan(root, out var plan) && plan is not null)
        {
            try { QuestPlanReceived?.Invoke(plan); }
            catch { /* UI callbacks cannot interrupt the relay receiver. */ }
        }
    }

    internal static bool TryReadQuestPlan(JsonElement root, out SquadQuestPlan? plan)
    {
        plan = null;
        if (root.ValueKind != JsonValueKind.Object
            || !ReadString(root, "type", out var kind) || kind != "questplan"
            || !ReadString(root, "id", out var id) || id.Length > 64
            || !ReadString(root, "name", out var name) || name.Length > 24
            || !ReadString(root, "mode", out var mode)
            || !ReadInt64(root, "at", out var at)
            || at < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 120000
            || at > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 120000
            || !root.TryGetProperty("quests", out var quests)
            || quests.ValueKind != JsonValueKind.Array
            || quests.GetArrayLength() > 80
            || !root.TryGetProperty("done", out var done)
            || done.ValueKind != JsonValueKind.Array
            || done.GetArrayLength() > 120) return false;
        var taskIds = new List<string>(), stepIds = new List<string>();
        foreach (var item in quests.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (value is null || !TaskIdPattern.IsMatch(value)) return false;
            taskIds.Add(value.ToLowerInvariant());
        }
        foreach (var item in done.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (value is null || !StepIdPattern.IsMatch(value)) return false;
            stepIds.Add(value.ToLowerInvariant());
        }
        var candidate = new SquadQuestPlan(id, name, mode,
            taskIds.Distinct(StringComparer.Ordinal).ToArray(),
            stepIds.Distinct(StringComparer.Ordinal).ToArray(), at);
        if (!ValidQuestPlan(new SquadQuestPlanSnapshot(
                candidate.Mode, candidate.Quests, candidate.Done))) return false;
        plan = candidate;
        return true;
    }

    internal static bool TryReadPose(JsonElement root, out SquadMemberPose? pose)
    {
        pose = null;
        if (root.ValueKind != JsonValueKind.Object
            || !ReadString(root, "id", out var id)
            || !ReadString(root, "name", out var name)
            || !ReadString(root, "map", out var map)
            || !root.TryGetProperty("position", out var position)
            || position.ValueKind != JsonValueKind.Object
            || !ReadDouble(position, "x", out var x)
            || !ReadDouble(position, "y", out var y)
            || !ReadDouble(position, "z", out var z)
            || !ReadDouble(root, "rotation", out var rotation)
            || !ReadInt64(root, "at", out var timestamp)) return false;

        // The deployed Worker does not forward uncertainty. 1000 means unknown,
        // NOT a measured 1000-metre error.
        var candidate = new SquadMemberPose(
            id, NormalizeName(name), NormalizeMapKey(map),
            x, y, z, Normalize360(rotation), 1000, timestamp);
        if (!IsValidPose(candidate) || candidate.MemberId.Length > 64
            || candidate.Map.Length > 64) return false;
        pose = candidate;
        return true;
    }

    // Portable normalization. This client is also linked into Beacon and
    // Launcher, which do not reference the WinForms LocalMapHost class.
    internal static string NormalizeMapKey(string? map)
    {
        var key = (map ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
        return key switch
        {
            "labs" or "lab" or "the-lab-dark" => "the-lab",
            "streets" or "streetsoftarkov" => "streets-of-tarkov",
            "groundzero" => "ground-zero",
            "night-factory" => "factory",
            _ => key
        };
    }

    private static bool IsValidPose(SquadMemberPose pose) =>
        !string.IsNullOrWhiteSpace(pose.MemberId)
        && !string.IsNullOrWhiteSpace(pose.Name)
        && !string.IsNullOrWhiteSpace(pose.Map)
        && double.IsFinite(pose.X)
        && double.IsFinite(pose.Y)
        && double.IsFinite(pose.Z)
        && double.IsFinite(pose.YawDegrees)
        && double.IsFinite(pose.UncertaintyMeters)
        && Math.Abs(pose.X) <= 1_000_000
        && Math.Abs(pose.Y) <= 1_000_000
        && Math.Abs(pose.Z) <= 1_000_000
        && pose.UncertaintyMeters is >= 0 and <= 1000
        && pose.SentAtUnixMilliseconds > 0;

    public static string NormalizeName(string? value)
    {
        var cleaned = new string((value ?? string.Empty)
            .Where(c => char.IsLetterOrDigit(c)
                || c is ' ' or '_' or '.' or '-' or '\'')
            .Take(24).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Joueur" : cleaned;
    }

    private static ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.SetRequestHeader("User-Agent", "RaidPilot-Squad/2.0");
        return socket;
    }

    private static Uri BuildEndpoint(string key, string name) =>
        new($"{SocketBaseUri}?team={Uri.EscapeDataString(key)}&name={Uri.EscapeDataString(name)}");

    private static async Task SendTextAsync(
        ClientWebSocket socket,
        string text,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool ReadString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        return parent.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && (value = element.GetString() ?? string.Empty).Length > 0;
    }

    private static bool ReadDouble(JsonElement parent, string name, out double value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static bool ReadInt32(JsonElement parent, string name, out int value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool ReadInt64(JsonElement parent, string name, out long value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out value);
    }

    private static double Normalize360(double degrees)
    {
        var value = degrees % 360.0;
        return value < 0 ? value + 360.0 : value;
    }

    private static void AbortAndDispose(ref ClientWebSocket? socket)
    {
        var old = socket;
        socket = null;
        if (old is null)
        {
            return;
        }
        try
        {
            old.Abort();
        }
        catch
        {
        }
        old.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _broadcastLease.Release();
        AbortAndDispose(ref _receiver);
        if (_receiverLoop is not null)
        {
            try
            {
                await _receiverLoop.ConfigureAwait(false);
            }
            catch
            {
            }
        }
        // Do not dispose the synchronization primitives here: a final
        // coalesced 5 Hz send can still be unwinding after cancellation. They
        // contain no unmanaged resources and are safely reclaimed with this client.
    }
}
