using System.Text.Json;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class QueryPackageExecuteHandlerIsolationTests
{
    [Fact]
    public async Task HandleAsync_UsesUniqueInfinityProfileWhenMarketingIsActive()
    {
        var executor = new RecordingExecutor();
        var store = new InMemoryProfileStore(new[]
        {
            Profile("Marketing", "Marketing"),
            Profile("InfinityRetailDB", "InfinityRetailDB"),
        });
        var settings = new AppSettings
        {
            TunnelId = "test-tunnel",
            ActiveDatabaseProfileName = "Marketing",
        };
        var handler = new QueryPackageExecuteHandler(
            store,
            new LiveDatabaseProfileResolver(store, settings),
            new FixedCatalog("infinity", "InfinityRetailDB"),
            new AlwaysValidSignature(),
            executor,
            new ReadClassifier(),
            new AllowAllPolicy(),
            new RequestValidator(settings),
            new TestBridgeLogger(),
            new NoopRequestTracker(),
            new LiveQueryActivityFeed());

        var response = await handler.HandleAsync(Command(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("InfinityRetailDB", executor.Profile?.DatabaseName);
    }

    private static BridgeCommand Command()
    {
        using var document = JsonDocument.Parse("""
            { "queryId": "infinity.catalog.search", "parameters": {} }
            """);
        return new BridgeCommand
        {
            ProtocolVersion = BridgeLimits.SupportedProtocolVersion,
            MessageType = MessageTypes.QueryPackageExecute,
            RequestId = Guid.NewGuid().ToString("N"),
            TunnelId = "test-tunnel",
            SentAtUtc = DateTime.UtcNow,
            Payload = document.RootElement.Clone(),
        };
    }

    private static DatabaseProfile Profile(string name, string database) => new()
    {
        Id = Guid.NewGuid(),
        ProfileName = name,
        ServerName = "local-sql",
        DatabaseName = database,
        IsEnabled = true,
        PermissionLevel = SqlPermissionLevel.ReadOnly,
        ConnectionKind = DatabaseConnectionKind.Local,
        AuthenticationMode = SqlAuthenticationMode.WindowsAuthentication,
    };

    private sealed class FixedCatalog(string system, string databaseProfile) : IQueryPackageCatalogClient
    {
        public Task<SignedQueryPackage?> GetAsync(string queryId, CancellationToken cancellationToken) =>
            Task.FromResult<SignedQueryPackage?>(new SignedQueryPackage
            {
                KeyId = "test",
                SignatureBase64 = "AA==",
                Definition = new QueryPackageDefinition
                {
                    QueryId = queryId,
                    Version = 1,
                    System = system,
                    DatabaseProfile = databaseProfile,
                    Sql = "SELECT 1",
                },
            });
    }

    private sealed class AlwaysValidSignature : IQueryPackageSignatureVerifier
    {
        public bool Verify(SignedQueryPackage package, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }

    private sealed class InMemoryProfileStore(IReadOnlyList<DatabaseProfile> profiles) : IDatabaseProfileStore
    {
        public DatabaseProfile? GetByName(string profileName) => profiles.FirstOrDefault(profile => profile.ProfileName == profileName);
        public DatabaseProfile? GetById(Guid id) => profiles.FirstOrDefault(profile => profile.Id == id);
        public IReadOnlyList<DatabaseProfile> GetAll() => profiles;
        public void Save(DatabaseProfile profile) => throw new NotSupportedException();
        public bool Delete(Guid id) => throw new NotSupportedException();
        public void Reload() { }
    }

    private sealed class RecordingExecutor : ISqlCommandExecutor
    {
        public DatabaseProfile? Profile { get; private set; }
        public Task<SqlExecutionResult> ExecuteAsync(DatabaseProfile profile, SqlExecutePayload request, CancellationToken cancellationToken)
        {
            Profile = profile;
            return Task.FromResult(new SqlExecutionResult { Success = true });
        }
    }

    private sealed class ReadClassifier : IQueryClassifier
    {
        public QueryClassification Classify(string sql) => QueryClassification.Read;
    }

    private sealed class AllowAllPolicy : IPermissionPolicy
    {
        public PermissionCheckResult Evaluate(DatabaseProfile profile, string sql, QueryClassification classification) => PermissionCheckResult.Allowed();
    }

    private sealed class NoopRequestTracker : IActiveRequestTracker
    {
        public int ActiveCount => 0;
        public void Register(string requestId, CancellationTokenSource cancellationTokenSource) { }
        public bool TryCancel(string requestId) => false;
        public void Complete(string requestId) { }
    }
}
