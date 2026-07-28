namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

public enum SnapshotSyncPhase
{
    Planned,
    Started,
    Completed,
    WaveCompleted,
}

public sealed record SnapshotSyncJobPlan(
    string SnapshotType,
    string DisplayName,
    int EstimatedSeconds);

public sealed record SnapshotSyncProgress(
    SnapshotSyncPhase Phase,
    IReadOnlyList<SnapshotSyncJobPlan>? Jobs = null,
    string? SnapshotType = null,
    string? DisplayName = null,
    int EstimatedSeconds = 0,
    bool Success = false,
    int RowCount = 0,
    string? Message = null,
    TimeSpan? Elapsed = null);
