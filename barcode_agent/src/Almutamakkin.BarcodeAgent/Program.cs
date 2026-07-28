using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Database;
using Almutamakkin.BarcodeAgent.Jobs;
using Almutamakkin.BarcodeAgent.Models;
using Almutamakkin.BarcodeAgent.Printing;
using Almutamakkin.BarcodeAgent.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var machineConfiguration = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "Almutamakkin",
    "BarcodeAgent",
    "appsettings.Production.json");
builder.Configuration
    .AddJsonFile(machineConfiguration, optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);
builder.Host.UseWindowsService(options => options.ServiceName = "Almutamakkin Barcode Agent");
builder.WebHost.UseUrls(builder.Configuration[$"{ServerOptions.SectionName}:Urls"] ?? "http://0.0.0.0:8787");

builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => !options.ConnectionString.Contains("REPLACE", StringComparison.OrdinalIgnoreCase), "Database connection string is not configured.")
    .ValidateOnStart();
builder.Services.AddOptions<PrinterOptions>()
    .Bind(builder.Configuration.GetSection(PrinterOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => !options.ApiKey.Contains("GENERATE", StringComparison.OrdinalIgnoreCase), "API key is not configured.")
    .ValidateOnStart();
builder.Services.AddOptions<JobStoreOptions>()
    .Bind(builder.Configuration.GetSection(JobStoreOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IProductRepository, SqlProductRepository>();
builder.Services.AddSingleton<IBarcodePrintability, BarcodePrintability>();
builder.Services.AddSingleton<ILabelRenderer, LabelRenderer>();
builder.Services.AddSingleton<IRawPrinter, WindowsRawPrinter>();
builder.Services.AddSingleton<PrintJobRegistry>();
builder.Services.AddSingleton<PrintQueueService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<PrintQueueService>());
builder.Services.AddSingleton<TestPrintJobRegistry>();
builder.Services.AddSingleton<TestPrintQueueService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TestPrintQueueService>());
builder.Services.AddRateLimiter(options =>
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
        var configured = builder.Configuration.GetValue<int?>($"{PrinterOptions.SectionName}:PrintRequestsPerMinute") ?? 10;
        limiter.PermitLimit = Math.Clamp(configured, 1, 120);
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});

var app = builder.Build();
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
    var response = new HealthResponse(
        databaseReady && printerStatus.Ready ? "ready" : "degraded",
        databaseReady ? "connected" : "unavailable",
        printerStatus.State,
        options.QueueName,
        $"{options.LabelWidthMm}x{options.LabelHeightMm}",
        printerStatus.Reason,
        printerStatus.QueuedJobs);
    return Results.Json(response, statusCode: databaseReady && printerStatus.Ready ? 200 : 503);
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
    var product = barId is > 0 and <= int.MaxValue ? await products.GetByBarIdAsync(barId, cancellationToken) : null;
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

app.MapGet("/", () => Results.Ok(new { service = "Almutamakkin Barcode Agent", api = "/api/v1" }));
app.Run();

public partial class Program;
