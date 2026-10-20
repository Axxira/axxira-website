using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RaidPilot.Overlay;

internal sealed class TarkovDevGpsClient : IAsyncDisposable
{
    private const int MaxInboundMessageBytes = 64 * 1024;
    private static readonly TimeSpan PositionSendTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PongSendTimeout = TimeSpan.FromSeconds(4);
    private static readonly Uri SocketBaseUri = new("wss://socket.tarkov.dev");
    private readonly string _remoteId;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private ClientWebSocket? _socket;
    private Task? _receiveTask;
    private int _disposeStarted;

    private bool IsDisposed => Volatile.Read(ref _disposeStarted) != 0;

    internal TarkovDevGpsClient(string remoteId)
    {
        _remoteId = remoteId;
    }

    internal async Task<ClientWebSocket> ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        await _connectGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_socket?.State == WebSocketState.Open)
            {
                return _socket;
            }

            if (_socket is not null)
            {
                _socket.Dispose();
                _socket = null;
            }

            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("User-Agent", "RaidPilot-Overlay/0.8.6");
            socket.Options.SetRequestHeader("Origin", "https://tarkov.dev");
            var endpoint = new Uri($"{SocketBaseUri}?sessionid={Uri.EscapeDataString(_remoteId)}-rp");
            try
            {
                await socket.ConnectAsync(endpoint, linkedCancellation.Token).ConfigureAwait(false);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            if (IsDisposed)
            {
                socket.Abort();
                socket.Dispose();
                throw new ObjectDisposedException(nameof(TarkovDevGpsClient));
            }

            _socket = socket;
            _receiveTask = Task.Run(() => ReceiveLoopAsync(socket, _lifetime.Token), CancellationToken.None);
            return socket;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    internal async Task SendPositionAsync(
        ScreenshotObservation observation,
        string normalizedMapName,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sendTimeout.CancelAfter(PositionSendTimeout);
        try
        {
            var socket = await ConnectAsync(sendTimeout.Token).ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(new
            {
                type = "command",
                sessionID = _remoteId,
                data = new
                {
                    type = "playerPosition",
                    map = normalizedMapName,
                    position = new
                    {
                        x = observation.X,
                        y = observation.Y,
                        z = observation.Z,
                    },
                    rotation = observation.YawDegrees,
                },
            });

            await SendTextAsync(
                payload,
                socket,
                sendTimeout.Token,
                abortSocketOnCancellation: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
            && !_lifetime.IsCancellationRequested)
        {
            throw new TimeoutException("Le service GPS tarkov.dev n'a pas accepté la position sous deux secondes.");
        }
    }

    private async Task SendTextAsync(
        string payload,
        ClientWebSocket socket,
        CancellationToken cancellationToken,
        bool abortSocketOnCancellation = false)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        var enteredSendGate = false;
        var sendStarted = false;
        try
        {
            await _sendGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
            enteredSendGate = true;
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (!ReferenceEquals(socket, _socket)
                || socket.State != WebSocketState.Open)
            {
                throw new WebSocketException("La connexion GPS tarkov.dev n'est pas ouverte.");
            }

            sendStarted = true;
            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (abortSocketOnCancellation && sendStarted)
        {
            // Cet appel avait réellement commencé à écrire sur ce socket.
            // N'interrompre ni un envoi concurrent qui détenait encore le
            // verrou, ni un nouveau socket créé entre-temps.
            TryAbort(socket);
            throw;
        }
        finally
        {
            if (enteredSendGate)
            {
                _sendGate.Release();
            }
        }
    }

    private static void TryAbort(ClientWebSocket socket)
    {
        try
        {
            socket.Abort();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var messageBuffer = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                        {
                            using var closeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            closeTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                            try
                            {
                                await socket.CloseOutputAsync(
                                    WebSocketCloseStatus.NormalClosure,
                                    "Closing",
                                    closeTimeout.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                socket.Abort();
                            }
                            catch (WebSocketException)
                            {
                                socket.Abort();
                            }
                        }
                        return;
                    }

                    if (messageBuffer.Length + result.Count > MaxInboundMessageBytes)
                    {
                        socket.Abort();
                        return;
                    }

                    messageBuffer.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(messageBuffer.ToArray());
                }
                catch (JsonException)
                {
                    // Un message serveur non-commandé mal formé ne doit pas
                    // tuer la boucle qui répond aux prochains pings.
                    continue;
                }

                using (document)
                {
                    if (document.RootElement.TryGetProperty("type", out var type)
                        && type.ValueKind == JsonValueKind.String
                        && string.Equals(type.GetString(), "ping", StringComparison.Ordinal))
                    {
                        using var pongTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken);
                        pongTimeout.CancelAfter(PongSendTimeout);
                        try
                        {
                            await SendTextAsync(
                                "{\"type\":\"pong\"}",
                                socket,
                                pongTimeout.Token,
                                abortSocketOnCancellation: true).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            // Même un pong qui attendait le verrou ne doit jamais
                            // bloquer la file d'envoi à vie. Après quatre secondes,
                            // fermer exactement ce socket force une reconnexion propre.
                            TryAbort(socket);
                            return;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
            // A later screenshot retries through ConnectAsync.
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested || IsDisposed)
        {
        }
        catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested || IsDisposed)
        {
        }
    }

    /// <summary>Bounded synchronous fallback for process/window shutdown.</summary>
    internal void Abort()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        var socket = Interlocked.Exchange(ref _socket, null);
        if (socket is not null)
        {
            try
            {
                socket.Abort();
            }
            finally
            {
                socket.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();

        await _connectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _sendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var socket = _socket;
                _socket = null;
                if (socket is not null)
                {
                    try
                    {
                        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                        {
                            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await socket.CloseOutputAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "RaidPilot GPS disabled",
                                closeTimeout.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        socket.Abort();
                    }
                    catch (WebSocketException)
                    {
                        socket.Abort();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    finally
                    {
                        socket.Dispose();
                    }
                }
            }
            finally
            {
                _sendGate.Release();
            }
        }
        finally
        {
            _connectGate.Release();
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }
            catch (WebSocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _lifetime.Dispose();
    }
}
