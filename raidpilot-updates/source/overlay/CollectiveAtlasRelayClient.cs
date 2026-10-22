using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RaidPilot.Overlay;

internal sealed class CollectiveAtlasRelayClient : IAsyncDisposable
{
    private const int MaximumInboundBytes =
        CollectiveAtlasProtocol.MaximumCandidateJsonBytes + 8192;
    private static readonly Uri SocketBaseUri =
        new("wss://socket.tarkov.dev");

    private readonly string _mapId;
    private readonly string _contributorId;
    private readonly string _sessionId;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _senderGate = new(1, 1);
    private readonly SemaphoreSlim _receiverGate = new(1, 1);
    private ClientWebSocket? _sender;
    private ClientWebSocket? _receiver;
    private Task? _senderHeartbeatTask;
    private Task? _receiverLoop;
    private int _disposed;

    internal CollectiveAtlasRelayClient(
        string mapId,
        string contributorId)
    {
        _mapId = CollectiveAtlasProtocol.NormalizeMapId(mapId);
        _contributorId =
            CollectiveAtlasProtocol.NormalizeContributorId(contributorId);
        if (_mapId.Length == 0 || _contributorId.Length < 16)
        {
            throw new ArgumentException(
                "Atlas relay identity/map invalid.");
        }
        _sessionId = BuildAtlasSessionId(_mapId);
    }

    internal event Action<CollectiveAtlasCandidate>? CandidateReceived;
    internal event Action<string>? HelloReceived;

    internal async Task StartReceivingAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_receiverLoop is { IsCompleted: false })
        {
            return;
        }

        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
        await EnsureReceiverConnectedAsync(linked.Token)
            .ConfigureAwait(false);
        _receiverLoop = Task.Run(
            () => ReceiverReconnectLoopAsync(_lifetime.Token),
            CancellationToken.None);
    }

    internal Task SendHelloAsync(
        CancellationToken cancellationToken = default) =>
        SendDataAsync(
            new
            {
                type = "raidpilotAtlasHello",
                protocol = CollectiveAtlasProtocol.ProtocolVersion,
                contributorId = _contributorId,
                map = _mapId,
            },
            cancellationToken);

    internal Task SendCandidateAsync(
        CollectiveAtlasCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (!CollectiveAtlasProtocol.TryValidate(
                candidate,
                out var normalized)
            || !string.Equals(
                normalized.MapId,
                _mapId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Invalid atlas candidate.",
                nameof(candidate));
        }

        return SendDataAsync(
            new
            {
                type = "raidpilotAtlasCandidate",
                protocol = CollectiveAtlasProtocol.ProtocolVersion,
                candidate = normalized,
            },
            cancellationToken);
    }

    internal static string BuildAtlasSessionId(string mapId)
    {
        var normalized =
            CollectiveAtlasProtocol.NormalizeMapId(mapId);
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(normalized));
        return "RPATLAS1-"
            + Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private async Task SendDataAsync(
        object data,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        await _senderGate.WaitAsync(timeout.Token)
            .ConfigureAwait(false);
        try
        {
            var socket =
                await EnsureSenderConnectedLockedAsync(
                    timeout.Token)
                    .ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(new
            {
                type = "command",
                sessionID = _sessionId,
                data,
            });
            if (Encoding.UTF8.GetByteCount(payload)
                > MaximumInboundBytes)
            {
                throw new InvalidOperationException(
                    "Atlas packet exceeds relay limit.");
            }

            try
            {
                await SendTextAsync(
                    socket,
                    payload,
                    timeout.Token)
                    .ConfigureAwait(false);
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

    private async Task ReceiverReconnectLoopAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? receiver = null;
            try
            {
                receiver =
                    await EnsureReceiverConnectedAsync(
                        cancellationToken)
                        .ConfigureAwait(false);
                await ReceiveLoopAsync(
                    receiver,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
            }
            finally
            {
                await _receiverGate.WaitAsync(
                    CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    if (ReferenceEquals(
                            receiver,
                            _receiver))
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
                await Task.Delay(
                    TimeSpan.FromSeconds(2),
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<ClientWebSocket>
        EnsureReceiverConnectedAsync(
            CancellationToken cancellationToken)
    {
        await _receiverGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
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
                    cancellationToken)
                    .ConfigureAwait(false);
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

    private async Task<ClientWebSocket>
        EnsureSenderConnectedLockedAsync(
            CancellationToken cancellationToken)
    {
        if (_sender?.State == WebSocketState.Open)
        {
            return _sender;
        }

        AbortAndDispose(ref _sender);
        var socket = CreateSocket();
        var senderSession = _sessionId
            + "-"
            + StableSenderSuffix(_contributorId);
        try
        {
            await socket.ConnectAsync(
                BuildEndpoint(senderSession),
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
        _sender = socket;
        _senderHeartbeatTask = Task.Run(
            () => SenderHeartbeatLoopAsync(
                socket,
                _lifetime.Token),
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
                        cancellationToken)
                        .ConfigureAwait(false);
                    if (result.MessageType
                        == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    if (message.Length + result.Count
                        > MaximumInboundBytes)
                    {
                        socket.Abort();
                        return;
                    }
                    message.Write(
                        buffer,
                        0,
                        result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType
                        == WebSocketMessageType.Text
                    && IsPing(message.ToArray()))
                {
                    await _senderGate.WaitAsync(
                        cancellationToken)
                        .ConfigureAwait(false);
                    try
                    {
                        if (ReferenceEquals(
                                socket,
                                _sender)
                            && socket.State
                                == WebSocketState.Open)
                        {
                            await SendTextAsync(
                                socket,
                                "{\"type\":\"pong\"}",
                                cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        _senderGate.Release();
                    }
                }
            }
        }
        catch
        {
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (!cancellationToken.IsCancellationRequested
            && socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType
                    == WebSocketMessageType.Close)
                {
                    return;
                }
                if (message.Length + result.Count
                    > MaximumInboundBytes)
                {
                    socket.Abort();
                    return;
                }
                message.Write(
                    buffer,
                    0,
                    result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType
                != WebSocketMessageType.Text)
            {
                continue;
            }

            var bytes = message.ToArray();
            if (IsPing(bytes))
            {
                await SendTextAsync(
                    socket,
                    "{\"type\":\"pong\"}",
                    cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            TryDispatch(bytes);
        }
    }

    private void TryDispatch(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(
                    "type",
                    out var envelopeType)
                || envelopeType.GetString() != "command"
                || !root.TryGetProperty(
                    "data",
                    out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty(
                    "type",
                    out var typeElement)
                || typeElement.ValueKind
                    != JsonValueKind.String)
            {
                return;
            }

            var type = typeElement.GetString();
            if (type == "raidpilotAtlasCandidate"
                && data.TryGetProperty(
                    "candidate",
                    out var candidateElement))
            {
                var candidate =
                    candidateElement
                        .Deserialize<
                            CollectiveAtlasCandidate>();
                if (CollectiveAtlasProtocol.TryValidate(
                        candidate,
                        out var normalized)
                    && !string.Equals(
                        normalized.ContributorId,
                        _contributorId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        normalized.MapId,
                        _mapId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CandidateReceived?.Invoke(normalized);
                }
                return;
            }

            if (type == "raidpilotAtlasHello"
                && data.TryGetProperty(
                    "protocol",
                    out var protocolElement)
                && protocolElement.TryGetInt32(
                    out var protocol)
                && protocol
                    == CollectiveAtlasProtocol.ProtocolVersion
                && data.TryGetProperty(
                    "contributorId",
                    out var contributorElement)
                && contributorElement.ValueKind
                    == JsonValueKind.String
                && data.TryGetProperty(
                    "map",
                    out var mapElement)
                && mapElement.ValueKind
                    == JsonValueKind.String)
            {
                var contributor =
                    CollectiveAtlasProtocol
                        .NormalizeContributorId(
                            contributorElement.GetString());
                var map =
                    CollectiveAtlasProtocol
                        .NormalizeMapId(
                            mapElement.GetString());
                if (contributor.Length >= 16
                    && !string.Equals(
                        contributor,
                        _contributorId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        map,
                        _mapId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    HelloReceived?.Invoke(contributor);
                }
            }
        }
        catch
        {
        }
    }

    private static bool IsPing(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.TryGetProperty(
                    "type",
                    out var type)
                && type.ValueKind
                    == JsonValueKind.String
                && type.GetString() == "ping";
        }
        catch
        {
            return false;
        }
    }

    private static ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader(
            "Origin",
            "https://tarkov.dev");
        socket.Options.SetRequestHeader(
            "User-Agent",
            "RaidPilot-CollectiveAtlas/1.0");
        return socket;
    }

    private static Uri BuildEndpoint(string sessionId) =>
        new(
            $"{SocketBaseUri}?sessionid="
            + Uri.EscapeDataString(sessionId));

    private static string StableSenderSuffix(
        string contributorId)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(contributorId));
        return Convert.ToHexString(
            bytes.AsSpan(0, 6));
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
            true,
            cancellationToken)
            .ConfigureAwait(false);
    }

    private static void AbortAndDispose(
        ref ClientWebSocket? socket)
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

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(
                ref _disposed,
                1) != 0)
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
                await _senderHeartbeatTask
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
