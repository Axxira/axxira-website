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

public sealed class SquadRelayClient : IAsyncDisposable
{
    private const int ProtocolVersion = 1;
    private const int MaximumInboundBytes = 32 * 1024;
    private static readonly Uri SocketBaseUri = new("wss://socket.tarkov.dev");

    private readonly string _roomCode;
    private readonly string _sessionId;
    private readonly string _memberId;
    private readonly string _name;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _senderGate = new(1, 1);
    private readonly SemaphoreSlim _receiverGate = new(1, 1);
    private ClientWebSocket? _sender;
    private ClientWebSocket? _receiver;
    private Task? _senderHeartbeatTask;
    private Task? _receiverLoop;
    private int _disposed;

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
        _sessionId = SquadRoomCode.ToSessionId(normalized);
        _memberId = memberId.Trim();
        _name = NormalizeName(name);
    }

    public string RoomCode => _roomCode;
    public string MemberId => _memberId;
    public string Name => _name;
    public bool IsReceiving => _receiverLoop is { IsCompleted: false };

    public event Action<SquadMemberPose>? PoseReceived;
    public event Action<bool>? ReceiverConnectionChanged;

    public async Task StartReceivingAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_receiverLoop is { IsCompleted: false })
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        await EnsureReceiverConnectedAsync(linked.Token).ConfigureAwait(false);
        _receiverLoop = Task.Run(
            () => ReceiverReconnectLoopAsync(_lifetime.Token),
            CancellationToken.None);
    }

    public async Task SendPoseAsync(
        SquadMemberPose pose,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(pose);

        if (!string.Equals(pose.MemberId, _memberId, StringComparison.Ordinal)
            || !string.Equals(pose.Name, _name, StringComparison.Ordinal)
            || !IsValidPose(pose))
        {
            throw new ArgumentException("Pose d'escouade invalide.", nameof(pose));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        await _senderGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var socket = await EnsureSenderConnectedLockedAsync(timeout.Token).ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(new
            {
                type = "command",
                sessionID = _sessionId,
                data = new
                {
                    type = "raidpilotSquadPose",
                    protocol = ProtocolVersion,
                    memberId = pose.MemberId,
                    name = pose.Name,
                    map = pose.Map,
                    x = pose.X,
                    y = pose.Y,
                    z = pose.Z,
                    yaw = pose.YawDegrees,
                    uncertainty = pose.UncertaintyMeters,
                    sentAtUnixMs = pose.SentAtUnixMilliseconds,
                },
            });
            try
            {
                await SendTextAsync(socket, payload, timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                AbortAndDispose(ref _sender);
                throw;
            }
        }
        finally
        {
            _senderGate.Release();
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
                    BuildEndpoint(_sessionId),
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

    private async Task<ClientWebSocket> EnsureSenderConnectedLockedAsync(
        CancellationToken cancellationToken)
    {
        if (_sender?.State == WebSocketState.Open)
        {
            return _sender;
        }

        AbortAndDispose(ref _sender);
        var socket = CreateSocket();
        var senderSession = _sessionId + "-" + StableSenderSuffix(_memberId);
        try
        {
            await socket.ConnectAsync(
                BuildEndpoint(senderSession),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
        _sender = socket;
        _senderHeartbeatTask = Task.Run(
            () => SenderHeartbeatLoopAsync(socket, _lifetime.Token),
            CancellationToken.None);
        return socket;
    }

    private async Task SenderHeartbeatLoopAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
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
                    if (!root.TryGetProperty("type", out var type)
                        || type.ValueKind != JsonValueKind.String
                        || !string.Equals(type.GetString(), "ping", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                await _senderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (ReferenceEquals(socket, _sender)
                        && socket.State == WebSocketState.Open)
                    {
                        await SendTextAsync(
                            socket,
                            "{\"type\":\"pong\"}",
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _senderGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (ObjectDisposedException)
        {
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
                if (root.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && string.Equals(type.GetString(), "ping", StringComparison.Ordinal))
                {
                    await SendTextAsync(
                        socket,
                        "{\"type\":\"pong\"}",
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (TryReadPose(root, out var pose)
                    && pose is not null
                    && !string.Equals(pose.MemberId, _memberId, StringComparison.Ordinal))
                {
                    PoseReceived?.Invoke(pose);
                }
            }
        }
    }

    private static bool TryReadPose(JsonElement root, out SquadMemberPose? pose)
    {
        pose = null;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var envelopeType)
            || envelopeType.ValueKind != JsonValueKind.String
            || !string.Equals(envelopeType.GetString(), "command", StringComparison.Ordinal)
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !ReadString(data, "type", out var dataType)
            || !string.Equals(dataType, "raidpilotSquadPose", StringComparison.Ordinal)
            || !ReadInt32(data, "protocol", out var protocol)
            || protocol != ProtocolVersion
            || !ReadString(data, "memberId", out var memberId)
            || !ReadString(data, "name", out var name)
            || !ReadString(data, "map", out var map)
            || !ReadDouble(data, "x", out var x)
            || !ReadDouble(data, "y", out var y)
            || !ReadDouble(data, "z", out var z)
            || !ReadDouble(data, "yaw", out var yaw)
            || !ReadDouble(data, "uncertainty", out var uncertainty)
            || !ReadInt64(data, "sentAtUnixMs", out var sentAt))
        {
            return false;
        }

        var candidate = new SquadMemberPose(
            memberId,
            NormalizeName(name),
            map,
            x,
            y,
            z,
            Normalize360(yaw),
            uncertainty,
            sentAt);
        if (!IsValidPose(candidate)
            || candidate.MemberId.Length > 64
            || candidate.Map.Length > 64)
        {
            return false;
        }

        pose = candidate;
        return true;
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
            .Where(c => !char.IsControl(c))
            .Take(24)
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Teammate" : cleaned;
    }

    private static ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "https://tarkov.dev");
        socket.Options.SetRequestHeader("User-Agent", "RaidPilot-Squad/1.0");
        return socket;
    }

    private static Uri BuildEndpoint(string sessionId) =>
        new($"{SocketBaseUri}?sessionid={Uri.EscapeDataString(sessionId)}");

    private static string StableSenderSuffix(string memberId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(memberId));
        return Convert.ToHexString(bytes.AsSpan(0, 6));
    }

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
        AbortAndDispose(ref _sender);
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
        if (_senderHeartbeatTask is not null)
        {
            try
            {
                await _senderHeartbeatTask.ConfigureAwait(false);
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
