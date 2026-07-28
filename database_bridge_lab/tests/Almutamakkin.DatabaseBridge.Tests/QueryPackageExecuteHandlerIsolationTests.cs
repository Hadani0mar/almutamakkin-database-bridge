using System.Text.Json;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class QueryPackageExecuteHandlerIsolationTests
{
    [Fact]
    public async Task HandleAsync_RejectsInfinityPackageWhenMarketingProfileIsActive()
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

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DatabaseProfileNotFound, response.Error?.Code);
        Assert.Null(executor.Profile);
    }

    [Fact]
    public async Task HandleAsync_RejectsMarketingPackageWhenInfinityProfileIsActive()
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
            ActiveDatabaseProfileName = "InfinityRetailDB",
        };
        var handler = CreateHandler(
            store,
            settings,
            executor,
            new FixedCatalog(
                "marketing",
                "Marketing",
                sql: "SELECT TOP (@limit) ITEM_ID FROM dbo.ITEMS_VIEW"));

        var response = await handler.HandleAsync(
            Command(
                "marketing.future_report.anything",
                """{ "limit": { "type": "int", "value": 7 } }"""),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DatabaseProfileNotFound, response.Error?.Code);
        Assert.Null(executor.Profile);
    }

    [Fact]
    public async Task HandleAsync_RejectsStoredProcedurePackageWithoutExecuting()
    {
        var executor = new RecordingExecutor();
        var store = new InMemoryProfileStore(new[] { Profile("Marketing", "Marketing") });
        var settings = new AppSettings { TunnelId = "test-tunnel", ActiveDatabaseProfileName = "Marketing" };
        var handler = CreateHandler(
            store,
            settings,
            executor,
            new FixedCatalog("marketing", "Marketing", sql: "SELECT 1; EXEC dbo.hidden_write"));

        var response = await handler.HandleAsync(Command("marketing.report.safe"), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.SqlPermissionDenied, response.Error?.Code);
        Assert.Null(executor.Profile);
    }

    [Fact]
    public async Task HandleAsync_RejectsPackageWhoseSystemDoesNotMatchItsResolvedProfile()
    {
        var executor = new RecordingExecutor();
        var store = new InMemoryProfileStore(new[] { Profile("Marketing", "Marketing") });
        var settings = new AppSettings { TunnelId = "test-tunnel", ActiveDatabaseProfileName = "Marketing" };
        var handler = CreateHandler(
            store,
            settings,
            executor,
            new FixedCatalog("infinity", "Marketing"));

        var response = await handler.HandleAsync(Command("infinity.catalog.search"), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DatabaseProfileNotFound, response.Error?.Code);
        Assert.Null(executor.Profile);
    }

    [Fact]
    public async Task HandleAsync_ExpandsIntArrayIntoBoundScalarParameters()
    {
        var executor = new RecordingExecutor();
        var store = new InMemoryProfileStore(new[] { Profile("Marketing", "Marketing") });
        var settings = new AppSettings { TunnelId = "test-tunnel", ActiveDatabaseProfileName = "Marketing" };
        var handler = CreateHandler(
            store,
            settings,
            executor,
            new FixedCatalog(
                "marketing",
                "Marketing",
                sql: "SELECT ITEM_ID FROM dbo.ITEMS_VIEW WHERE ITEM_ID IN (@employeeIds)"));

        var response = await handler.HandleAsync(
            Command(
                "marketing.employee_invoice_lines",
                """{ "employeeIds": { "type": "int[]", "value": [3, 8, 21] } }"""),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(
            "SELECT ITEM_ID FROM dbo.ITEMS_VIEW WHERE ITEM_ID IN (@employeeIds__0, @employeeIds__1, @employeeIds__2)",
            executor.Request?.Sql);
        Assert.DoesNotContain("employeeIds", executor.Request!.Parameters.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(3, executor.Request.Parameters.Count);
        Assert.All(executor.Request.Parameters.Values, parameter => Assert.Equal("int", parameter.Type));
    }

    [Fact]
    public async Task HandleAsync_RejectsOversizedIntArrayBeforeExecuting()
    {
        var executor = new RecordingExecutor();
        var store = new InMemoryProfileStore(new[] { Profile("Marketing", "Marketing") });
        var settings = new AppSettings { TunnelId = "test-tunnel", ActiveDatabaseProfileName = "Marketing" };
        var handler = CreateHandler(
            store,
            settings,
            executor,
            new FixedCatalog(
                "marketing",
                "Marketing",
                sql: "SELECT ITEM_ID FROM dbo.ITEMS_VIEW WHERE ITEM_ID IN (@employeeIds)"));
        var values = string.Join(",", Enumerable.Range(1, BridgeLimits.MaximumIntArrayParameterItems + 1));

        var response = await handler.HandleAsync(
            Command(
                "marketing.employee_invoice_lines",
                $$"""{ "employeeIds": { "type": "int[]", "value": [{{values}}] } }"""),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.InvalidMessage, response.Error?.Code);
        Assert.Null(executor.Profile);
    }

    private static QueryPackageExecuteHandler CreateHandler(
        IDatabaseProfileStore store,
        AppSettings settings,
        RecordingExecutor executor,
        IQueryPackageCatalogClient catalog) =>
        new(
            store,
            new LiveDatabaseProfileResolver(store, settings),
            catalog,
            new AlwaysValidSignature(),
            executor,
            new ReadClassifier(),
            new AllowAllPolicy(),
            new RequestValidator(settings),
            new TestBridgeLogger(),
            new NoopRequestTracker(),
            new LiveQueryActivityFeed());

    private static BridgeCommand Command(
        string queryId = "infinity.catalog.search",
        string parameters = "{}")
    {
        using var document = JsonDocument.Parse($$"""
            { "queryId": "{{queryId}}", "parameters": {{parameters}} }
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

    private sealed class FixedCatalog(
        string system,
        string databaseProfile,
        string sql = "SELECT 1") : IQueryPackageCatalogClient
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
                    Sql = sql,
                    Parameters = ParametersFor(sql),
                },
            });

        private static List<QueryPackageParameterDefinition> ParametersFor(string sql)
        {
            var parameters = new List<QueryPackageParameterDefinition>();
            if (sql.Contains("@limit", StringComparison.Ordinal))
            {
                parameters.Add(new QueryPackageParameterDefinition
                {
                    Name = "limit",
                    Type = "int",
                    Required = true,
                });
            }

            if (sql.Contains("@employeeIds", StringComparison.Ordinal))
            {
                parameters.Add(new QueryPackageParameterDefinition
                {
                    Name = "employeeIds",
                    Type = "int[]",
                    Required = true,
                });
            }

            return parameters;
        }
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
        public SqlExecutePayload? Request { get; private set; }
        public Task<SqlExecutionResult> ExecuteAsync(DatabaseProfile profile, SqlExecutePayload request, CancellationToken cancellationToken)
        {
            Profile = profile;
            Request = request;
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
