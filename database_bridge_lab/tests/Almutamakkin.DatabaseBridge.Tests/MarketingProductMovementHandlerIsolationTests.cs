using System.Text.Json;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class MarketingProductMovementHandlerIsolationTests
{
    [Fact]
    public async Task HandleAsync_UsesUniqueMarketingProfile_WhenInfinityIsTheActiveProfile()
    {
        var marketing = Profile("Marketing", "Marketing", "marketing-host");
        var infinity = Profile("InfinityRetailDB", "InfinityRetailDB", "infinity-host");
        var store = new InMemoryProfileStore(new[] { marketing, infinity });
        var executor = new RecordingExecutor();
        var handler = new MarketingProductMovementHandler(
            store,
            new LiveDatabaseProfileResolver(store, new AppSettings
            {
                TunnelId = "test-tunnel",
                ActiveDatabaseProfileName = "InfinityRetailDB",
            }),
            executor,
            new RequestValidator(new AppSettings { TunnelId = "test-tunnel" }),
            new TestBridgeLogger());

        var response = await handler.HandleAsync(Command(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("Marketing", executor.Profile?.DatabaseName);
    }

    private static DatabaseProfile Profile(string name, string database, string server) => new()
    {
        Id = Guid.NewGuid(),
        ProfileName = name,
        DatabaseName = database,
        ServerName = server,
        IsEnabled = true,
        PermissionLevel = SqlPermissionLevel.ReadOnly,
        ConnectionKind = DatabaseConnectionKind.Local,
        AuthenticationMode = SqlAuthenticationMode.WindowsAuthentication,
    };

    private static BridgeCommand Command()
    {
        using var document = JsonDocument.Parse("""
            {
              "productId": 7,
              "startDate": "2026-06-01",
              "endDate": "2026-06-30",
              "granularity": "daily"
            }
            """);
        return new BridgeCommand
        {
            ProtocolVersion = BridgeLimits.SupportedProtocolVersion,
            MessageType = MessageTypes.MarketingProductMovement,
            RequestId = "REQ-MOVEMENT-001",
            TunnelId = "test-tunnel",
            SentAtUtc = DateTime.UtcNow,
            Payload = document.RootElement.Clone(),
        };
    }

    private sealed class InMemoryProfileStore : IDatabaseProfileStore
    {
        private readonly IReadOnlyList<DatabaseProfile> _profiles;

        public InMemoryProfileStore(IReadOnlyList<DatabaseProfile> profiles) => _profiles = profiles;

        public DatabaseProfile? GetByName(string profileName) => _profiles.FirstOrDefault(
            profile => string.Equals(profile.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
        public DatabaseProfile? GetById(Guid id) => _profiles.FirstOrDefault(profile => profile.Id == id);
        public IReadOnlyList<DatabaseProfile> GetAll() => _profiles;
        public void Save(DatabaseProfile profile) => throw new NotSupportedException();
        public bool Delete(Guid id) => throw new NotSupportedException();
        public void Reload() { }
    }

    private sealed class RecordingExecutor : ISqlCommandExecutor
    {
        public DatabaseProfile? Profile { get; private set; }

        public Task<SqlExecutionResult> ExecuteAsync(
            DatabaseProfile profile,
            SqlExecutePayload request,
            CancellationToken cancellationToken)
        {
            Profile = profile;
            return Task.FromResult(new SqlExecutionResult { Success = true });
        }
    }
}
