using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed record SqlServerInstanceInfo(
    string DisplayName,
    string DataSource,
    string? InstanceName,
    bool IsDefaultInstance,
    bool IsLocal);

public sealed record SqlDatabaseInfo(
    string Name,
    string? CompatibilityHint);

public interface ISqlServerDiscovery
{
    IReadOnlyList<SqlServerInstanceInfo> DiscoverLocalInstances();

    Task<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(
        string dataSource,
        SqlAuthenticationMode authenticationMode,
        string? userName,
        string? plainPassword,
        bool trustServerCertificate,
        bool encryptConnection,
        CancellationToken cancellationToken);
}
