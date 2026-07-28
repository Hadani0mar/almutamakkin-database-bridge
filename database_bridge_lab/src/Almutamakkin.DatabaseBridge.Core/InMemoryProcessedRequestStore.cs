using System.Collections.Concurrent;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class InMemoryProcessedRequestStore : IProcessedRequestStore
{
    private readonly ConcurrentDictionary<string, ProcessedRequestEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeSpan _retention;
    private readonly object _cleanupLock = new();
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public InMemoryProcessedRequestStore(TimeSpan? retention = null)
    {
        _retention = retention ?? TimeSpan.FromHours(BridgeLimits.ProcessedRequestRetentionHours);
    }

    public bool TryGetResponse(string requestId, out BridgeResponse? response)
    {
        response = null;
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return false;
        }

        CleanupExpiredIfNeeded();

        if (_entries.TryGetValue(requestId, out var entry) && !entry.IsExpired(_retention))
        {
            response = entry.Response;
            return true;
        }

        return false;
    }

    public void Store(string requestId, BridgeResponse response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(response);

        CleanupExpiredIfNeeded();
        _entries[requestId] = new ProcessedRequestEntry(DateTime.UtcNow, response);
    }

    public void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - _retention;

        foreach (var pair in _entries)
        {
            if (pair.Value.StoredAtUtc < cutoff)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }

        _lastCleanupUtc = DateTime.UtcNow;
    }

    private void CleanupExpiredIfNeeded()
    {
        if (DateTime.UtcNow - _lastCleanupUtc < TimeSpan.FromMinutes(1))
        {
            return;
        }

        lock (_cleanupLock)
        {
            if (DateTime.UtcNow - _lastCleanupUtc < TimeSpan.FromMinutes(1))
            {
                return;
            }

            CleanupExpired();
        }
    }

    private sealed record ProcessedRequestEntry(DateTime StoredAtUtc, BridgeResponse Response)
    {
        public bool IsExpired(TimeSpan retention) =>
            DateTime.UtcNow - StoredAtUtc > retention;
    }
}
