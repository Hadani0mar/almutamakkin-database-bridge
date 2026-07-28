using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public interface IPermissionPolicy
{
    PermissionCheckResult Evaluate(DatabaseProfile profile, string sql, QueryClassification classification);
}

public sealed record PermissionCheckResult
{
    public bool IsAllowed { get; init; }

    public string? ErrorMessage { get; init; }

    public static PermissionCheckResult Allowed() =>
        new() { IsAllowed = true };

    public static PermissionCheckResult Denied(string errorMessage) =>
        new() { IsAllowed = false, ErrorMessage = errorMessage };
}
