using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public interface IProcessedRequestStore
{
    bool TryGetResponse(string requestId, out BridgeResponse? response);

    void Store(string requestId, BridgeResponse response);

    void CleanupExpired();
}
