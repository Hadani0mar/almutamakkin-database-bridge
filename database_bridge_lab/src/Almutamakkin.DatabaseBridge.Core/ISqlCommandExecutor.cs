using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public interface ISqlCommandExecutor
{
    Task<SqlExecutionResult> ExecuteAsync(
        DatabaseProfile profile,
        SqlExecutePayload request,
        CancellationToken cancellationToken);
}
