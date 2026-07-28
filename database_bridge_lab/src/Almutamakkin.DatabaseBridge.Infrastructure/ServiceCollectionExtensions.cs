using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;
using Almutamakkin.DatabaseBridge.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseBridgeInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var settingsStore = new AppSettingsStore();
        var settings = settingsStore.Load();

        services.AddSingleton(settingsStore);
        services.AddSingleton(settings);

        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<IQueryPackageCatalogClient, SupabaseQueryPackageCatalogClient>();
        services.AddSingleton<IQueryPackageSignatureVerifier, RsaQueryPackageSignatureVerifier>();
        services.AddSingleton<IConnectionStringBuilder, SqlConnectionStringBuilderService>();
        services.AddSingleton<ISqlCommandExecutor, SqlCommandExecutor>();
        services.AddSingleton<IProductPhotoService, SqlProductPhotoService>();
        services.AddSingleton<IDatabaseConnectionTester, SqlDatabaseConnectionTester>();
        services.AddSingleton<ISqlServerDiscovery, SqlServerDiscoveryService>();
        services.AddSingleton<IDatabaseProfileStore, JsonDatabaseProfileStore>();
        services.AddSingleton<ILiveDatabaseProfileResolver, LiveDatabaseProfileResolver>();
        services.AddSingleton<ILiveQueryActivityFeed, LiveQueryActivityFeed>();
        services.AddSingleton<IBridgeLogger, FileBridgeLogger>();
        services.AddSingleton<SupabaseSnapshotIngestClient>();
        services.AddSingleton<SupabaseLiveIngestClient>();
        services.AddSingleton<ISnapshotFingerprintStore, JsonSnapshotFingerprintStore>();
        services.AddSingleton<IChangeCursorStore, JsonChangeCursorStore>();
        services.AddSingleton<ActivitySnapshotSyncService>();
        services.AddSingleton<LiveActiveInvoiceSyncService>();
        services.AddSingleton<ChangeWatchService>();
        services.AddSingleton<DomainWatchService>();
        services.AddSingleton<SupabaseChangeTicketClient>();
        services.AddSingleton<LocalTestCommandTransport>();
        services.AddSingleton<WebSocketCommandTransport>();
        services.AddSingleton<SupabaseBridgeTransport>();
        services.AddSingleton<ICommandTransport>(provider =>
            settings.TransportMode switch
            {
                TransportMode.WebSocket => provider.GetRequiredService<WebSocketCommandTransport>(),
                TransportMode.SupabaseTunnel => provider.GetRequiredService<SupabaseBridgeTransport>(),
                _ => provider.GetRequiredService<LocalTestCommandTransport>(),
            });

        return services;
    }
}
