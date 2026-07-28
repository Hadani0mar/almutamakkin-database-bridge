using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public interface ICommandHandler
{
    string MessageType { get; }

    Task<BridgeResponse> HandleAsync(
        BridgeCommand command,
        CancellationToken cancellationToken);
}
