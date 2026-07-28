using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Database;
using Almutamakkin.BarcodeAgent.Jobs;
using Almutamakkin.BarcodeAgent.Models;
using Almutamakkin.BarcodeAgent.Printing;
using Almutamakkin.BarcodeBridge.Configuration;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Almutamakkin.DatabaseBridge.App;

/// <summary>
/// Hosts barcode printer core services for Supabase tunnel commands,
/// independent of the optional LAN/Kestrel UI tab.
/// </summary>
public sealed class PrinterBridgeFacade : IPrinterBridgeFacade, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ServiceProvider? _provider;
    private CancellationTokenSource? _hostedCts;
    private BridgeSettings? _settings;

    public async Task<PrinterBridgeOperationResult> HealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var runtime = await EnsureRuntimeAsync(cancellationToken);
            if (runtime is null)
            {
                return PrinterBridgeOperationResult.Fail(
                    ErrorCodes.PrinterNotConfigured,
                    "إعدادات الطابعة غير مكتملة. افتح تبويب الطابعة واختر الطابعة واحفظ الإعدادات.");
            }

            var products = runtime.GetRequiredService<IProductRepository>();
            var printer = runtime.GetRequiredService<IRawPrinter>();
            var options = runtime.GetRequiredService<IOptions<PrinterOptions>>().Value;
            var databaseReady = await products.CanConnectAsync(cancellationToken);
            var status = printer.GetStatus();
            var ready = databaseReady && status.Ready;
            return PrinterBridgeOperationResult.Ok(new
            {
                status = ready ? "ready" : "degraded",
                database = databaseReady ? "connected" : "unavailable",
                printer = status.State,
                printerQueue = options.QueueName,
                labelSize = $"{options.LabelWidthMm}x{options.LabelHeightMm}",
                printerReason = status.Reason,
                queuedWindowsJobs = status.QueuedJobs,
            });
        }
        catch (Exception ex)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.InternalError,
                "تعذر فحص حالة الطابعة: " + ex.Message,
                retryable: true);
        }
    }

    public async Task<PrinterBridgeOperationResult> SearchProductsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var runtime = await EnsureRuntimeAsync(cancellationToken);
        if (runtime is null)
        {
            return NotConfigured();
        }

        var normalized = query.Trim();
        if (normalized.Length is < 2 or > 200)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.InvalidMessage,
                "نص البحث يجب أن يكون بين حرفين و200 حرف.");
        }

        var take = Math.Clamp(limit <= 0 ? 20 : limit, 1, 20);
        var items = await runtime.GetRequiredService<IProductRepository>()
            .SearchAsync(normalized, take, cancellationToken);
        return PrinterBridgeOperationResult.Ok(new { items });
    }

    public async Task<PrinterBridgeOperationResult> GetProductsByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken)
    {
        var runtime = await EnsureRuntimeAsync(cancellationToken);
        if (runtime is null)
        {
            return NotConfigured();
        }

        var normalized = barcode.Trim();
        if (normalized.Length is < 1 or > 128)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.InvalidMessage,
                "الباركود غير صالح.");
        }

        var items = await runtime.GetRequiredService<IProductRepository>()
            .GetByBarcodeAsync(normalized, cancellationToken);
        return PrinterBridgeOperationResult.Ok(new { items });
    }

    public async Task<PrinterBridgeOperationResult> GetProductByBarIdAsync(
        long barId,
        CancellationToken cancellationToken)
    {
        var runtime = await EnsureRuntimeAsync(cancellationToken);
        if (runtime is null)
        {
            return NotConfigured();
        }

        if (barId is <= 0 or > int.MaxValue)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.PrinterProductNotFound,
                "معرّف الباركود غير صالح.");
        }

        var item = await runtime.GetRequiredService<IProductRepository>()
            .GetByBarIdAsync(barId, cancellationToken);
        if (item is null)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.PrinterProductNotFound,
                "لم يُعثر على الصنف.");
        }

        return PrinterBridgeOperationResult.Ok(new { item });
    }

    public async Task<PrinterBridgeOperationResult> SubmitPrintAsync(
        string requestId,
        long barId,
        int copies,
        CancellationToken cancellationToken)
    {
        var runtime = await EnsureRuntimeAsync(cancellationToken);
        if (runtime is null)
        {
            return NotConfigured();
        }

        var options = runtime.GetRequiredService<IOptions<PrinterOptions>>().Value;
        if (barId is <= 0 or > int.MaxValue)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.InvalidMessage,
                "معرّف الباركود غير صالح.");
        }

        if (copies < 1 || copies > options.MaximumCopies)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.InvalidMessage,
                $"عدد النسخ يجب أن يكون بين 1 و {options.MaximumCopies}.");
        }

        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length is < 8 or > 128)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.InvalidRequestId,
                "معرف طلب الطباعة غير صالح.");
        }

        var products = runtime.GetRequiredService<IProductRepository>();
        var product = await products.GetByBarIdAsync(barId, cancellationToken);
        if (product is null)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.PrinterProductNotFound,
                "لم يُعثر على الصنف.");
        }

        if (!product.Printable)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.PrinterNotPrintable,
                product.PrintabilityReason ?? "لا يمكن طباعة هذا الباركود.");
        }

        var queue = runtime.GetRequiredService<PrintQueueService>();
        var submitted = await queue.SubmitAsync(
            new PrintJobRequest(requestId, barId, copies),
            cancellationToken);

        if (submitted.Conflict)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.PrinterConflict,
                "تم استخدام نفس معرف الطلب مع بيانات مختلفة.");
        }

        if (submitted.Busy)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.PrinterQueueFull,
                "طابور الطباعة ممتلئ. أعد المحاولة بعد لحظات.",
                retryable: true);
        }

        if (submitted.Job.Status == "failed")
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.PrinterNotReady,
                submitted.Job.Error ?? "فشلت مهمة الطباعة.",
                retryable: true);
        }

        return PrinterBridgeOperationResult.Ok(ToJobPayload(submitted.Job));
    }

    public async Task<PrinterBridgeOperationResult> SubmitTestPrintAsync(
        string requestId,
        string barcode,
        int copies,
        CancellationToken cancellationToken)
    {
        var runtime = await EnsureRuntimeAsync(cancellationToken);
        if (runtime is null)
        {
            return NotConfigured();
        }

        var normalized = barcode.Trim();
        if (normalized.Length is < 1 or > 14 || !normalized.All(char.IsDigit))
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.InvalidMessage,
                "الباركود التجريبي يجب أن يتكون من 1 إلى 14 رقماً.");
        }

        if (copies is < 1 or > 5)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.InvalidMessage,
                "عدد نسخ التجربة يجب أن يكون بين 1 و5.");
        }

        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length is < 8 or > 128)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.InvalidRequestId,
                "معرف طلب الطباعة غير صالح.");
        }

        var queue = runtime.GetRequiredService<TestPrintQueueService>();
        var submitted = await queue.SubmitAsync(
            new TestPrintJobRequest(requestId, normalized, copies),
            cancellationToken);

        if (submitted.Conflict)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.PrinterConflict,
                "تم استخدام نفس معرف الطلب مع بيانات مختلفة.");
        }

        if (submitted.Busy)
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.PrinterQueueFull,
                "طابور الطباعة ممتلئ. أعد المحاولة بعد لحظات.",
                retryable: true);
        }

        if (submitted.Job.Status == "failed")
        {
            return PrinterBridgeOperationResult.Fail(
                ErrorCodes.PrinterNotReady,
                submitted.Job.Error ?? "فشلت مهمة الطباعة التجريبية.",
                retryable: true);
        }

        return PrinterBridgeOperationResult.Ok(ToJobPayload(submitted.Job));
    }

    private static object ToJobPayload(PrintJobResponse job) => new
    {
        jobId = job.JobId,
        requestId = job.RequestId,
        status = job.Status,
        barId = job.BarId,
        itemId = job.ItemId,
        copies = job.Copies,
        barcode = job.Barcode,
        windowsJobId = job.WindowsJobId,
        error = job.Error,
        updatedAtUtc = job.UpdatedAtUtc,
        message = job.Status,
    };

    private static PrinterBridgeOperationResult NotConfigured() =>
        PrinterBridgeOperationResult.Fail(
            ErrorCodes.PrinterNotConfigured,
            "إعدادات الطابعة غير مكتملة. افتح تبويب الطابعة واختر الطابعة واحفظ الإعدادات.");

    private async Task<IServiceProvider?> EnsureRuntimeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = new EncryptedSettingsStore();
            var settings = store.LoadOrCreate();
            var errors = settings.Validate();
            if (errors.Count != 0)
            {
                return null;
            }

            if (_provider is not null && SettingsEqual(_settings, settings))
            {
                return _provider;
            }

            await DisposeRuntimeUnlockedAsync();

            var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        RegisterCoreServices(services, settings, store.DataDirectory);
            var provider = services.BuildServiceProvider();
            _hostedCts = new CancellationTokenSource();
            foreach (var hosted in provider.GetServices<IHostedService>())
            {
                await hosted.StartAsync(_hostedCts.Token);
            }

            _provider = provider;
            _settings = settings.Copy();
            return _provider;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool SettingsEqual(BridgeSettings? left, BridgeSettings right)
    {
        if (left is null) return false;
        return string.Equals(left.SqlServer, right.SqlServer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Database, right.Database, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Username, right.Username, StringComparison.Ordinal)
            && string.Equals(left.Password, right.Password, StringComparison.Ordinal)
            && string.Equals(left.PrinterName, right.PrinterName, StringComparison.Ordinal)
            && left.Port == right.Port;
    }

    private static void RegisterCoreServices(
        IServiceCollection services,
        BridgeSettings settings,
        string dataDirectory)
    {
        var database = new DatabaseOptions
        {
            ConnectionString = settings.BuildConnectionString(),
            CommandTimeoutSeconds = 15,
        };
        var printer = new PrinterOptions
        {
            QueueName = settings.PrinterName,
            LabelWidthMm = 38,
            LabelHeightMm = 25,
            Dpi = 203,
            Speed = 3,
            Density = 6,
            GapMm = 2,
            MaximumCopies = 20,
            QueueCapacity = 50,
            PrintRequestsPerMinute = 10,
            BusinessNameFont = "Tahoma",
        };
        var jobs = new JobStoreOptions
        {
            DataDirectory = Path.Combine(dataDirectory, "jobs"),
            RetentionHours = 24,
        };

        services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(database));
        services.AddSingleton<IOptions<PrinterOptions>>(Options.Create(printer));
        services.AddSingleton<IOptions<JobStoreOptions>>(Options.Create(jobs));
        services.AddSingleton<IProductRepository, SqlProductRepository>();
        services.AddSingleton<IBarcodePrintability, BarcodePrintability>();
        services.AddSingleton<ILabelRenderer, LabelRenderer>();
        services.AddSingleton<IRawPrinter, WindowsRawPrinter>();
        services.AddSingleton<PrintJobRegistry>();
        services.AddSingleton<PrintQueueService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PrintQueueService>());
        services.AddSingleton<TestPrintJobRegistry>();
        services.AddSingleton<TestPrintQueueService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<TestPrintQueueService>());
    }

    private async Task DisposeRuntimeUnlockedAsync()
    {
        if (_provider is null) return;
        try
        {
            _hostedCts?.Cancel();
            foreach (var hosted in _provider.GetServices<IHostedService>())
            {
                await hosted.StopAsync(CancellationToken.None);
            }
        }
        catch
        {
            // Best-effort shutdown before rebuild.
        }

        await _provider.DisposeAsync();
        _provider = null;
        _settings = null;
        _hostedCts?.Dispose();
        _hostedCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await DisposeRuntimeUnlockedAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
