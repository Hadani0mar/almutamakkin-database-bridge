using System.Text.Json;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class InfinityProductMovementHandlerIsolationTests
{
    [Fact]
    public async Task HandleAsync_RejectsInfinityRequest_WhenMarketingIsTheActiveProfile()
    {
        var store = new InMemoryProfileStore(new[]
        {
            Profile("Marketing", "Marketing"),
            Profile("InfinityRetailDB", "InfinityRetailDB"),
        });
        var executor = new RecordingExecutor();
        var handler = new InfinityProductMovementHandler(
            store,
            new LiveDatabaseProfileResolver(store, new AppSettings
            {
                TunnelId = "test-tunnel",
                ActiveDatabaseProfileName = "Marketing",
            }),
            executor,
            new RequestValidator(new AppSettings { TunnelId = "test-tunnel" }),
            new TestBridgeLogger());

        var response = await handler.HandleAsync(Command(), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DatabaseProfileNotFound, response.Error?.Code);
        Assert.Null(executor.Profile);
    }

    [Fact]
    public async Task HandleAsync_SearchTermUsesPostedViewsAndSeparatesReturns()
    {
        var store = new InMemoryProfileStore(new[] { Profile("InfinityRetailDB", "InfinityRetailDB") });
        var executor = new RecordingExecutor();
        var handler = new InfinityProductMovementHandler(
            store,
            new LiveDatabaseProfileResolver(store, new AppSettings { TunnelId = "test-tunnel" }),
            executor,
            new RequestValidator(new AppSettings { TunnelId = "test-tunnel" }),
            new TestBridgeLogger());

        var response = await handler.HandleAsync(SearchCommand(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(executor.Request);
        Assert.Contains("SALES.Data_View_SalesInvoiceItems", executor.Request!.Sql);
        Assert.Contains("SALES.Data_View_SalesInvoices", executor.Request.Sql);
        Assert.Contains("invoice.IsPosted = 1", executor.Request.Sql);
        Assert.Contains("returnedBaseQuantity", executor.Request.Sql);
        Assert.Contains("item.QYT * item.UnitBaseQYT", executor.Request.Sql);
        Assert.Equal("8411047108659", executor.Request.Parameters["searchTerm"].Value);
    }

    private static DatabaseProfile Profile(string name, string database) => new()
    {
        Id = Guid.NewGuid(),
        ProfileName = name,
        DatabaseName = database,
        ServerName = "local-sql",
        IsEnabled = true,
        PermissionLevel = SqlPermissionLevel.ReadOnly,
        ConnectionKind = DatabaseConnectionKind.Local,
        AuthenticationMode = SqlAuthenticationMode.WindowsAuthentication,
    };

    private static BridgeCommand Command()
    {
        using var document = JsonDocument.Parse("""
            { "productId": 7, "startDate": "2026-06-01", "endDate": "2026-06-30", "granularity": "weekly" }
            """);
        return new BridgeCommand
        {
            ProtocolVersion = BridgeLimits.SupportedProtocolVersion,
            MessageType = MessageTypes.InfinityProductMovement,
            RequestId = "REQ-INFINITY-MOVEMENT-001",
            TunnelId = "test-tunnel",
            SentAtUtc = DateTime.UtcNow,
            Payload = document.RootElement.Clone(),
        };
    }

    private static BridgeCommand SearchCommand()
    {
        using var document = JsonDocument.Parse("""
            { "searchTerm": "8411047108659", "startDate": "2026-06-01", "endDate": "2026-06-30", "granularity": "daily" }
            """);
        return new BridgeCommand
        {
            ProtocolVersion = BridgeLimits.SupportedProtocolVersion,
            MessageType = MessageTypes.InfinityProductMovement,
            RequestId = "REQ-INFINITY-MOVEMENT-SEARCH-001",
            TunnelId = "test-tunnel",
            SentAtUtc = DateTime.UtcNow,
            Payload = document.RootElement.Clone(),
        };
    }

    private sealed class InMemoryProfileStore : IDatabaseProfileStore
    {
        private readonly IReadOnlyList<DatabaseProfile> _profiles;
        public InMemoryProfileStore(IReadOnlyList<DatabaseProfile> profiles) => _profiles = profiles;
        public DatabaseProfile? GetByName(string profileName) => _profiles.FirstOrDefault(p => string.Equals(p.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
        public DatabaseProfile? GetById(Guid id) => _profiles.FirstOrDefault(p => p.Id == id);
        public IReadOnlyList<DatabaseProfile> GetAll() => _profiles;
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
}
