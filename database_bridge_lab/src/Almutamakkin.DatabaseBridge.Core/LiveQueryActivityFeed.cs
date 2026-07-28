using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// In-flight live SQL request shown on the bridge dashboard.
/// Completed requests are removed so the list stays uncluttered.
/// </summary>
public sealed record LiveQueryActivity(
    string RequestId,
    DateTime StartedAtUtc,
    string System,
    DatabaseConnectionKind ConnectionKind,
    string DisplayName);

public interface ILiveQueryActivityFeed
{
    void Begin(LiveQueryActivity activity);

    void End(string requestId);

    IReadOnlyList<LiveQueryActivity> GetActive();
}

public sealed class LiveQueryActivityFeed : ILiveQueryActivityFeed
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LiveQueryActivity> _active = new(StringComparer.Ordinal);

    public void Begin(LiveQueryActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (string.IsNullOrWhiteSpace(activity.RequestId))
        {
            return;
        }

        lock (_sync)
        {
            _active[activity.RequestId] = activity;
        }
    }

    public void End(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        lock (_sync)
        {
            _active.Remove(requestId);
        }
    }

    public IReadOnlyList<LiveQueryActivity> GetActive()
    {
        lock (_sync)
        {
            return _active.Values
                .OrderBy(activity => activity.StartedAtUtc)
                .ToList();
        }
    }
}
