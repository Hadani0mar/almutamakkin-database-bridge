using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public interface IDatabaseConnectionTester
{
    Task<DatabaseConnectionTestResult> TestAsync(
        DatabaseProfile profile,
        CancellationToken cancellationToken);
}

public sealed record DatabaseConnectionTestResult
{
    public required bool Success { get; init; }

    public required string Message { get; init; }

    public string? Details { get; init; }

    public string? DatabaseName { get; init; }

    public string? ServerName { get; init; }

    public string? LoginName { get; init; }
}
