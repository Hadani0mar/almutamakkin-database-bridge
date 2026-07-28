using System.Net;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Database;
using Almutamakkin.BarcodeAgent.Jobs;
using Almutamakkin.BarcodeAgent.Models;
using Almutamakkin.BarcodeAgent.Printing;
using Almutamakkin.BarcodeAgent.Security;
using Almutamakkin.BarcodeBridge.Configuration;
using Almutamakkin.BarcodeBridge.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeBridge.Server;

public sealed class BridgeServerController(BridgeLogHub logs, string dataDirectory) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WebApplication? _application;

    public bool IsRunning => _application is not null;

    public async Task StartAsync(
        BridgeSettings settings,
        IPAddress? lanAddress,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_application is not null) return;
            var errors = settings.Validate();
            if (errors.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(BridgeServerController).Assembly.FullName,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = Environments.Production
            });
            // Bind all interfaces; pairing QR uses the detected LAN IPv4.
            builder.WebHost.UseUrls($"http://0.0.0.0:{settings.Port}");
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            builder.Logging.AddProvider(new BridgeLoggerProvider(logs));

            RegisterServices(builder.Services, settings, dataDirectory);
            var app = builder.Build();
            ConfigurePipeline(app);
            try
            {
                await app.StartAsync(cancellationToken);
                _application = app;
                logs.Add(LogLevel.Information, lanAddress is null
                    ? $"الخادم يعمل على المنفذ {settings.Port}. لم يُكتشف عنوان شبكة محلية بعد — وصّل الجهاز بشبكة Wi‑Fi أو Ethernet."
                    : $"الخادم يعمل على كل الواجهات؛ عنوان الربط للهاتف: {lanAddress}:{settings.Port}.");
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var app = _application;
            if (app is null) return;
            _application = null;
            try
            {
                await app.StopAsync(cancellationToken);
            }
            finally
            {
                await app.DisposeAsync();
                logs.Add(LogLevel.Information, "تم إيقاف الخادم.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void RegisterServices(IServiceCollection services, BridgeSettings settings, string dataDirectory)
    {
        var database = new DatabaseOptions
        {
            ConnectionString = settings.BuildConnectionString(),
            CommandTimeoutSeconds = 15
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
            BusinessNameFont = "Tahoma"
        };
        var security = new SecurityOptions
        {
            ApiKey = settings.ApiKey,
            HeaderName = "X-Almutamakkin-Key",
            AllowedNetworks =
            [
                "127.0.0.0/8",
                "::1/128",
                "10.0.0.0/8",
                "172.16.0.0/12",
                "192.168.0.0/16"
            ]
        };
        var jobs = new JobStoreOptions
        {
            DataDirectory = Path.Combine(dataDirectory, "jobs"),
            RetentionHours = 24
        };

        services.AddProblemDetails();
        services.AddMemoryCache();
        services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(database));
        services.AddSingleton<IOptions<PrinterOptions>>(Options.Create(printer));
        services.AddSingleton<IOptions<SecurityOptions>>(Options.Create(security));
        services.AddSingleton<IOptions<JobStoreOptions>>(Options.Create(jobs));
        services.AddSingleton<IProductRepository, SqlProductRepository>();
        services.AddSingleton<IBarcodePrintability, BarcodePrintability>();
        services.AddSingleton<ILabelRenderer, LabelRenderer>();
        services.AddSingleton<IRawPrinter, WindowsRawPrinter>();
        services.AddSingleton<PrintJobRegistry>();
        services.AddSingleton<PrintQueueService>();
        services.AddHostedService(provider => provider.GetRequiredService<PrintQueueService>());
        services.AddSingleton<TestPrintJobRegistry>();
        services.AddSingleton<TestPrintQueueService>();
        services.AddHostedService(provider => provider.GetRequiredService<TestPrintQueueService>());
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("api", limiter =>
            {
                limiter.PermitLimit = 120;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
            options.AddFixedWindowLimiter("print", limiter =>
            {
                limiter.PermitLimit = printer.PrintRequestsPerMinute;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
        });
    }

    private static void ConfigurePipeline(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseMiddleware<ApiSecurityMiddleware>();
        app.UseRateLimiter();

        var api = app.MapGroup("/api/v1").RequireRateLimiting("api");
        api.MapTestPrintEndpoints();
        api.MapGet("/health", async (
            IProductRepository products,
            IRawPrinter printer,
            IOptions<PrinterOptions> printerOptions,
            CancellationToken cancellationToken) =>
        {
            var databaseReady = await products.CanConnectAsync(cancellationToken);
            var printerStatus = printer.GetStatus();
            var options = printerOptions.Value;
            var ready = databaseReady && printerStatus.Ready;
            return Results.Json(new HealthResponse(
                ready ? "ready" : "degraded",
                databaseReady ? "connected" : "unavailable",
                printerStatus.State,
                options.QueueName,
                $"{options.LabelWidthMm}x{options.LabelHeightMm}",
                printerStatus.Reason,
                printerStatus.QueuedJobs), statusCode: ready ? 200 : 503);
        });

        api.MapGet("/products/search", async (
            string? q,
            int? limit,
            IProductRepository products,
            CancellationToken cancellationToken) =>
        {
            var query = q?.Trim() ?? string.Empty;
            if (query.Length is < 2 or > 200)
                return Results.Problem(statusCode: 400, title: "Search query must contain 2 to 200 characters.");
            var take = limit ?? 20;
            if (take is < 1 or > 20)
                return Results.Problem(statusCode: 400, title: "Limit must be between 1 and 20.");
            return Results.Ok(await products.SearchAsync(query, take, cancellationToken));
        });

        api.MapGet("/products/barcode/{barcode}", async (
            string barcode,
            IProductRepository products,
            CancellationToken cancellationToken) =>
        {
            var normalized = barcode.Trim();
            if (normalized.Length is < 1 or > 128)
                return Results.Problem(statusCode: 400, title: "Barcode is invalid.");
            var matches = await products.GetByBarcodeAsync(normalized, cancellationToken);
            return matches.Count == 0
                ? Results.Problem(statusCode: 404, title: "No product matches this barcode.")
                : Results.Ok(matches);
        });

        api.MapGet("/products/{barId:long}", async (
            long barId,
            IProductRepository products,
            CancellationToken cancellationToken) =>
        {
            var product = barId is > 0 and <= int.MaxValue
                ? await products.GetByBarIdAsync(barId, cancellationToken)
                : null;
            return product is null
                ? Results.Problem(statusCode: 404, title: "Barcode variant was not found.")
                : Results.Ok(product);
        });

        api.MapPost("/print-jobs", async (
            PrintJobRequest request,
            PrintQueueService queue,
            IProductRepository products,
            IOptions<PrinterOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (request.BarId <= 0 || request.BarId > int.MaxValue)
                return Results.Problem(statusCode: 400, title: "barId must be a positive number.");
            if (request.Copies < 1 || request.Copies > options.Value.MaximumCopies)
                return Results.Problem(statusCode: 400, title: $"copies must be between 1 and {options.Value.MaximumCopies}.");
            if (request.EffectiveRequestId.Length is < 8 or > 128 ||
                !Regex.IsMatch(request.EffectiveRequestId, "^[A-Za-z0-9_.:-]+$", RegexOptions.CultureInvariant))
                return Results.Problem(statusCode: 400, title: "requestId is invalid.");

            var product = await products.GetByBarIdAsync(request.BarId, cancellationToken);
            if (product is null)
                return Results.Problem(statusCode: 404, title: "Barcode variant was not found.");
            if (!product.Printable)
                return Results.Problem(statusCode: 422, title: "Barcode cannot be printed on a 38 mm label.", detail: product.PrintabilityReason);

            var submitted = await queue.SubmitAsync(request, cancellationToken);
            if (submitted.Conflict)
                return Results.Conflict(new ProblemDetails { Status = 409, Title = "requestId was already used with a different print payload." });
            if (submitted.Busy)
                return Results.Problem(statusCode: 429, title: "Print queue is full. Retry shortly with a new requestId.");
            if (submitted.Job.Status == "failed")
                return Results.Json(submitted.Job, statusCode: 503);
            return Results.Accepted($"/api/v1/print-jobs/{submitted.Job.JobId}", submitted.Job);
        }).RequireRateLimiting("print");

        api.MapGet("/print-jobs/{jobId}", (string jobId, PrintJobRegistry registry) =>
        {
            var job = registry.Get(jobId);
            return job is null
                ? Results.Problem(statusCode: 404, title: "Print job was not found.")
                : Results.Ok(job);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }
}
