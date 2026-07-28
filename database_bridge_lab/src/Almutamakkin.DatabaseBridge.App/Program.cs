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
        try
        {
            ApplicationConfiguration.Initialize();

            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(ConfigureServices)
                .Build();

            var mainForm = host.Services.GetRequiredService<MainForm>();
            Application.Run(mainForm);
        }
        catch (Exception exception)
        {
            var logPath = StartupFailureReporter.Write(exception);
            MessageBox.Show(
                $"تعذر بدء جسر المتمكن. تم حفظ تفاصيل الخطأ في:\n{logPath}",
                "جسر المتمكن",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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

internal static class StartupFailureReporter
{
    public static string Write(Exception exception)
    {
        try
        {
            var directory = LabPaths.EnsureLogsDirectory();
            var path = Path.Combine(directory, "startup-errors.log");
            File.AppendAllText(
                path,
                $"[{DateTimeOffset.UtcNow:O}] {exception}\r\n\r\n");
            return path;
        }
        catch
        {
            return "تعذر إنشاء سجل محلي.";
        }
    }
}
