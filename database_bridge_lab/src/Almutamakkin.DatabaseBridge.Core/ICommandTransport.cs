using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public interface ICommandTransport
{
    event Func<BridgeCommand, CancellationToken, Task>? CommandReceived;

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task SendResponseAsync(BridgeResponse response, CancellationToken cancellationToken);
}
