using System.Text.Json;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class SqlExecuteHandlerProfileIsolationTests
{
    [Theory]
    [InlineData("Marketing", "Marketing")]
    [InlineData("InfinityRetailDB", "InfinityRetailDB")]
    public async Task HandleAsync_UsesTheActiveProfileOnlyWhenItMatchesTheRequestedSystem(
        string requestedProfile,
        string expectedDatabase)
    {
        var profiles = new[]
        {
            Profile(requestedProfile, expectedDatabase, "local-sql"),
            Profile($"{requestedProfile}__remote", expectedDatabase, "remote-sql"),
        };
        var executor = new RecordingExecutor();
        var handler = CreateHandler(profiles, executor, requestedProfile);

        var response = await handler.HandleAsync(
            Command(requestedProfile),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(executor.Profile);
        Assert.Equal(expectedDatabase, executor.Profile!.DatabaseName);
        Assert.Equal("local-sql", executor.Profile.ServerName);
    }

    [Fact]
    public async Task HandleAsync_DoesNotFallbackToNetworkWhenNoLiveProfileIsSelected()
    {
        var executor = new RecordingExecutor();
        var handler = CreateHandler(
            new[] { Profile("Marketing__remote", "Marketing", "remote-sql") },
            executor);

        var response = await handler.HandleAsync(Command("Marketing"), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Null(executor.Profile);
    }

    [Fact]
    public async Task HandleAsync_RoutesCanonicalRequestToExplicitlySelectedNetworkProfile()
    {
        var executor = new RecordingExecutor();
        var handler = CreateHandler(
            new[]
            {
                Profile("Marketing", "Marketing", "local-sql"),
                Profile("Marketing__remote", "Marketing", "remote-sql"),
            },
            executor,
            activeProfileName: "Marketing__remote");

        var response = await handler.HandleAsync(Command("Marketing"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("remote-sql", executor.Profile!.ServerName);
    }

    [Fact]
    public async Task HandleAsync_RoutesAnotherSystemToItsUniqueEnabledProfile()
    {
        var executor = new RecordingExecutor();
        var handler = CreateHandler(
            new[]
            {
                Profile("Marketing", "Marketing", "local-sql"),
                Profile("InfinityRetailDB", "InfinityRetailDB", "local-sql"),
            },
            executor,
            activeProfileName: "Marketing");

        var response = await handler.HandleAsync(Command("InfinityRetailDB"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("InfinityRetailDB", executor.Profile!.DatabaseName);
    }

    [Fact]
    public async Task HandleAsync_RoutesAnotherSystemUsingTheSelectedNetworkConnectionKind()
    {
        var executor = new RecordingExecutor();
        var handler = CreateHandler(
            new[]
            {
                Profile("Marketing", "Marketing", "local-sql"),
                Profile("Marketing__remote", "Marketing", "remote-sql"),
                Profile("InfinityRetailDB", "InfinityRetailDB", "local-sql"),
                Profile("InfinityRetailDB__remote", "InfinityRetailDB", "remote-sql"),
            },
            executor,
            activeProfileName: "Marketing__remote");

        var response = await handler.HandleAsync(Command("InfinityRetailDB"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("remote-sql", executor.Profile!.ServerName);
        Assert.Equal("InfinityRetailDB", executor.Profile.DatabaseName);
    }

    [Fact]
    public async Task HandleAsync_RecoversFromStaleActiveSelectionUsingOnlyUniqueRequestedSystemProfile()
    {
        var executor = new RecordingExecutor();
        var handler = CreateHandler(
            new[]
            {
                Profile("InfinityRetailDB", "InfinityRetailDB", "remote-sql"),
                Profile("Marketing", "Marketing", "remote-sql"),
            },
            executor,
            activeProfileName: "RemovedOrRenamedProfile");

        var response = await handler.HandleAsync(Command("InfinityRetailDB"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(executor.Profile);
        Assert.Equal("InfinityRetailDB", executor.Profile!.DatabaseName);
        Assert.Equal("remote-sql", executor.Profile.ServerName);
    }

    [Fact]
    public async Task HandleAsync_AppliesCatalogOverrideEvenWhenCatalogIsUnknownToDiscovery()
    {
        var executor = new RecordingExecutor();
        var handler = CreateHandler(
            new[] { Profile("Marketing", "Marketing", "local-sql") },
            executor,
            activeProfileName: "Marketing");

        var response = await handler.HandleAsync(
            Command("Marketing", catalog: "DoesNotExistDb"),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("DoesNotExistDb", executor.Profile!.DatabaseName);
    }

    [Fact]
    public async Task HandleAsync_AppliesCatalogOverrideOnMarketingProfile()
    {
        var executor = new RecordingExecutor();
        var handler = CreateHandler(
            new[] { Profile("Marketing", "Marketing", "local-sql") },
            executor,
            activeProfileName: "Marketing");

        var response = await handler.HandleAsync(
            Command("Marketing", catalog: "Marketing2024"),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("Marketing2024", executor.Profile!.DatabaseName);
        Assert.Equal("Marketing", executor.Profile.ProfileName);
    }

    [Fact]
    public void GetSystem_TreatsMarketingBackupProfileAsMarketing()
    {
        var store = new InMemoryProfileStore(
            new[] { Profile("Marketing", "Marketing2024", "local-sql") });
        var resolver = new LiveDatabaseProfileResolver(
            store,
            new AppSettings { TunnelId = "test-tunnel", ActiveDatabaseProfileName = "Marketing" });

        Assert.Equal("marketing", resolver.GetSystem(store.GetAll()[0]));
        Assert.NotNull(resolver.Resolve("Marketing"));
    }

    private static SqlExecuteHandler CreateHandler(
        IReadOnlyList<DatabaseProfile> profiles,
        RecordingExecutor executor,
        string? activeProfileName = null)
    {
        var store = new InMemoryProfileStore(profiles);
        var settings = new AppSettings
        {
            TunnelId = "test-tunnel",
            ActiveDatabaseProfileName = activeProfileName,
        };
        return new(
            store,
            new LiveDatabaseProfileResolver(store, settings),
            executor,
            new ReadClassifier(),
            new AllowAllPolicy(),
            new RequestValidator(new AppSettings { TunnelId = "test-tunnel" }),
            new TestBridgeLogger(),
            new NoopRequestTracker(),
            new LiveQueryActivityFeed());
    }

    private static DatabaseProfile Profile(
        string profileName,
        string databaseName,
        string serverName) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProfileName = profileName,
            ServerName = serverName,
            DatabaseName = databaseName,
            IsEnabled = true,
            PermissionLevel = SqlPermissionLevel.ReadOnly,
            ConnectionKind = serverName == "remote-sql"
                ? DatabaseConnectionKind.Network
                : DatabaseConnectionKind.Local,
            AuthenticationMode = SqlAuthenticationMode.WindowsAuthentication,
        };

    private static BridgeCommand Command(string databaseProfile, string? catalog = null)
    {
        var catalogJson = catalog is null
            ? string.Empty
            : $"""
              ,
              "catalog": "{catalog}"
              """;
        using var document = JsonDocument.Parse($$"""
            {
              "databaseProfile": "{{databaseProfile}}",
              "sql": "SELECT 1",
              "timeoutSeconds": 30,
              "maxRows": 1000{{catalogJson}}
            }
            """);
        return new BridgeCommand
        {
            ProtocolVersion = BridgeLimits.SupportedProtocolVersion,
            MessageType = MessageTypes.SqlExecute,
            RequestId = Guid.NewGuid().ToString("N"),
            TunnelId = "test-tunnel",
            SentAtUtc = DateTime.UtcNow,
            Payload = document.RootElement.Clone(),
        };
    }

    private sealed class InMemoryProfileStore : IDatabaseProfileStore
    {
        private readonly IReadOnlyList<DatabaseProfile> _profiles;

        public InMemoryProfileStore(IReadOnlyList<DatabaseProfile> profiles) =>
            _profiles = profiles;

        public DatabaseProfile? GetByName(string profileName) => _profiles.FirstOrDefault(
            profile => string.Equals(profile.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));

        public DatabaseProfile? GetById(Guid id) => _profiles.FirstOrDefault(profile => profile.Id == id);

        public IReadOnlyList<DatabaseProfile> GetAll() => _profiles;

        public void Save(DatabaseProfile profile) => throw new NotSupportedException();

        public bool Delete(Guid id) => throw new NotSupportedException();

        public void Reload()
        {
        }
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

    private sealed class ReadClassifier : IQueryClassifier
    {
        public QueryClassification Classify(string sql) => QueryClassification.Read;
    }

    private sealed class AllowAllPolicy : IPermissionPolicy
    {
        public PermissionCheckResult Evaluate(
            DatabaseProfile profile,
            string sql,
            QueryClassification classification) => PermissionCheckResult.Allowed();
    }

    private sealed class NoopRequestTracker : IActiveRequestTracker
    {
        public int ActiveCount => 0;

        public void Register(string requestId, CancellationTokenSource cancellationTokenSource)
        {
        }

        public bool TryCancel(string requestId) => false;

        public void Complete(string requestId)
        {
        }
    }
}
