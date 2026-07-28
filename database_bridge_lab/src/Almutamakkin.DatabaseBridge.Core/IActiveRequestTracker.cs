namespace Almutamakkin.DatabaseBridge.Core;

public interface IActiveRequestTracker
{
    void Register(string requestId, CancellationTokenSource cancellationTokenSource);

    bool TryCancel(string requestId);

    void Complete(string requestId);

    int ActiveCount { get; }
}
