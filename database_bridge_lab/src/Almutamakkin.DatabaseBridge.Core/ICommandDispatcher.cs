using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public interface ICommandDispatcher
{
    Task<BridgeResponse> DispatchAsync(
        BridgeCommand command,
        CancellationToken cancellationToken);
}
