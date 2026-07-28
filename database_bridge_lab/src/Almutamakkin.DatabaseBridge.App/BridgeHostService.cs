using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.App;

public sealed class BridgeHostService
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly IBridgeLogger _logger;
    private ICommandTransport? _transport;

    public BridgeHostService(ICommandDispatcher dispatcher, IBridgeLogger logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public void Attach(ICommandTransport transport)
    {
        Detach();

        _transport = transport;
        _transport.CommandReceived += OnCommandReceivedAsync;
    }

    public void Detach()
    {
        if (_transport is null)
        {
            return;
        }

        _transport.CommandReceived -= OnCommandReceivedAsync;
        _transport = null;
    }

    private async Task OnCommandReceivedAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        if (_transport is null)
        {
            return;
        }

        try
        {
            var response = await _dispatcher.DispatchAsync(command, cancellationToken);
            await _transport.SendResponseAsync(response, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to process command {command.RequestId}.", ex);
        }
    }
}
