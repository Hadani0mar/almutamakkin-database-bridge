using System.Net.WebSockets;
using System.Text;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class WebSocketCommandTransport : ICommandTransport, IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly object _sync = new();
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoopTask;
    private bool _isConnected;

    public WebSocketCommandTransport(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public event Func<BridgeCommand, CancellationToken, Task>? CommandReceived;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _isConnected
                    && _webSocket?.State == WebSocketState.Open;
            }
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebSocketUrl))
        {
            throw new InvalidOperationException(
                "WebSocket transport is not configured. Set webSocketUrl in appsettings.json.");
        }

        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);

        var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(new Uri(_settings.WebSocketUrl), cancellationToken)
            .ConfigureAwait(false);

        var receiveLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        lock (_sync)
        {
            _webSocket = webSocket;
            _receiveLoopCts = receiveLoopCts;
            _isConnected = true;
            _receiveLoopTask = Task.Run(
                () => ReceiveLoopAsync(receiveLoopCts.Token),
                CancellationToken.None);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        Task? receiveLoopTask;
        ClientWebSocket? webSocket;
        CancellationTokenSource? receiveLoopCts;

        lock (_sync)
        {
            receiveLoopTask = _receiveLoopTask;
            webSocket = _webSocket;
            receiveLoopCts = _receiveLoopCts;
            _receiveLoopTask = null;
            _webSocket = null;
            _receiveLoopCts = null;
            _isConnected = false;
        }

        if (receiveLoopCts is not null)
        {
            await receiveLoopCts.CancelAsync().ConfigureAwait(false);
            receiveLoopCts.Dispose();
        }

        if (receiveLoopTask is not null)
        {
            try
            {
                await receiveLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when shutting down the receive loop.
            }
        }

        if (webSocket is not null)
        {
            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Disconnect requested.",
                        cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore close races during shutdown.
                }
            }

            webSocket.Dispose();
        }
    }

    public async Task SendResponseAsync(BridgeResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        ClientWebSocket? webSocket;
        lock (_sync)
        {
            webSocket = _webSocket;
        }

        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket transport is offline.");
        }

        var payload = BridgeJson.Serialize(response);
        var bytes = Encoding.UTF8.GetBytes(payload);
        await webSocket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() =>
        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];

        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? webSocket;
            lock (_sync)
            {
                webSocket = _webSocket;
            }

            if (webSocket is null || webSocket.State != WebSocketState.Open)
            {
                break;
            }

            try
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult receiveResult;

                do
                {
                    receiveResult = await webSocket.ReceiveAsync(buffer, cancellationToken)
                        .ConfigureAwait(false);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        await HandleDisconnectAndReconnectAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    messageStream.Write(buffer, 0, receiveResult.Count);
                }
                while (!receiveResult.EndOfMessage);

                if (receiveResult.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(messageStream.ToArray());
                var command = BridgeJson.DeserializeCommand(json);
                if (command is null)
                {
                    continue;
                }

                var handlers = CommandReceived;
                if (handlers is null)
                {
                    continue;
                }

                foreach (var handler in handlers.GetInvocationList()
                             .Cast<Func<BridgeCommand, CancellationToken, Task>>())
                {
                    await handler(command, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await HandleDisconnectAndReconnectAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task HandleDisconnectAndReconnectAsync(CancellationToken cancellationToken)
    {
        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested
            || string.IsNullOrWhiteSpace(_settings.WebSocketUrl))
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ignore reconnect attempts during shutdown.
        }
        catch
        {
            // Reconnect failures are handled by the host/UI layer in lab v1.
        }
    }
}
