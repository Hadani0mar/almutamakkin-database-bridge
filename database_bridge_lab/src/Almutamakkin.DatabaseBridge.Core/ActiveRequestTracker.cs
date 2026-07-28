using System.Collections.Concurrent;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class ActiveRequestTracker : IActiveRequestTracker
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeRequests = new(StringComparer.Ordinal);

    public int ActiveCount => _activeRequests.Count;

    public void Register(string requestId, CancellationTokenSource cancellationTokenSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(cancellationTokenSource);

        _activeRequests[requestId] = cancellationTokenSource;
    }

    public bool TryCancel(string requestId)
    {
        if (!_activeRequests.TryRemove(requestId, out var cts))
        {
            return false;
        }

        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }

        return true;
    }

    public void Complete(string requestId)
    {
        if (_activeRequests.TryRemove(requestId, out var cts))
        {
            cts.Dispose();
        }
    }
}
