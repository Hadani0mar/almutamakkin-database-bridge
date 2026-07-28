using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class LocalTestCommandTransport : ICommandTransport
{
    private readonly object _sync = new();
    private bool _isConnected;

    public event Func<BridgeCommand, CancellationToken, Task>? CommandReceived;
    public event Action<BridgeResponse>? ResponseSent;

    public BridgeResponse? LastResponse { get; private set; }

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _isConnected;
            }
        }
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _isConnected = true;
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _isConnected = false;
        }

        return Task.CompletedTask;
    }

    public Task SendResponseAsync(BridgeResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            LastResponse = response;
        }

        ResponseSent?.Invoke(response);
        return Task.CompletedTask;
    }

    public async Task<BridgeResponse?> SubmitTestCommandAsync(
        BridgeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureConnected();

        var handlers = CommandReceived;
        if (handlers is null)
        {
            throw new InvalidOperationException(
                "No command handlers are registered for LocalTestCommandTransport.");
        }

        foreach (var handler in handlers.GetInvocationList()
                     .Cast<Func<BridgeCommand, CancellationToken, Task>>())
        {
            await handler(command, cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            return LastResponse;
        }
    }

    public async Task<BridgeResponse?> SubmitTestCommandAsync(
        string json,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var command = BridgeJson.DeserializeCommand(json)
            ?? throw new InvalidOperationException("Unable to deserialize bridge command JSON.");

        return await SubmitTestCommandAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureConnected()
    {
        lock (_sync)
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException(
                    "Local test transport is not connected. Call ConnectAsync first.");
            }
        }
    }
}
