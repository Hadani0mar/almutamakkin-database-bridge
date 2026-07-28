using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Infrastructure;
using Almutamakkin.DatabaseBridge.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Almutamakkin.DatabaseBridge.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        var mainForm = host.Services.GetRequiredService<MainForm>();
        Application.Run(mainForm);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddDatabaseBridgeInfrastructure();

        services.AddSingleton<QueryClassifier>();
        services.AddSingleton<IQueryClassifier>(sp => sp.GetRequiredService<QueryClassifier>());
        services.AddSingleton<IPermissionPolicy, PermissionPolicy>();
        services.AddSingleton<IRequestValidator, RequestValidator>();
        services.AddSingleton<IActiveRequestTracker, ActiveRequestTracker>();
        services.AddSingleton<IProcessedRequestStore>(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            return new InMemoryProcessedRequestStore(
                TimeSpan.FromHours(settings.ProcessedRequestRetentionHours));
        });

        services.AddSingleton<ICommandHandler, BridgeHealthHandler>();
        services.AddSingleton<ICommandHandler, DatabaseTestHandler>();
        services.AddSingleton<ICommandHandler, DatabaseListHandler>();
        services.AddSingleton<ICommandHandler, SqlExecuteHandler>();
        services.AddSingleton<ICommandHandler, QueryPackageExecuteHandler>();
        services.AddSingleton<ICommandHandler, MarketingProductMovementHandler>();
        services.AddSingleton<ICommandHandler, InfinityProductMovementHandler>();
        services.AddSingleton<ICommandHandler, ProductPhotoHandler>();
        services.AddSingleton<ICommandHandler, ProductPhotoUpsertHandler>();
        services.AddSingleton<ICommandHandler, ChangesProbeHandler>();
        services.AddSingleton<ICommandHandler, ChangesPullHandler>();
        services.AddSingleton<IPrinterBridgeFacade, PrinterBridgeFacade>();
        services.AddSingleton<ICommandHandler, PrinterHealthHandler>();
        services.AddSingleton<ICommandHandler, PrinterProductsSearchHandler>();
        services.AddSingleton<ICommandHandler, PrinterProductsByBarcodeHandler>();
        services.AddSingleton<ICommandHandler, PrinterProductsByBarIdHandler>();
        services.AddSingleton<ICommandHandler, PrinterPrintSubmitHandler>();
        services.AddSingleton<ICommandHandler, PrinterTestSubmitHandler>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<BridgeHostService>();
        services.AddHttpClient<GitHubReleaseUpdateChecker>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        services.AddTransient<MainForm>();
        services.AddTransient<DatabaseProfilesForm>();
        services.AddTransient<TestConsoleForm>();
    }
}
