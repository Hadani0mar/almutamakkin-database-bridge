using System.Text.RegularExpressions;
using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Models;
using Almutamakkin.BarcodeAgent.Printing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Jobs;

public static partial class TestPrintEndpoints
{
    public static RouteGroupBuilder MapTestPrintEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/printer-health", (
            IRawPrinter printer,
            IOptions<PrinterOptions> printerOptions) =>
        {
            var printerStatus = printer.GetStatus();
            var options = printerOptions.Value;
            return Results.Json(new HealthResponse(
                printerStatus.Ready ? "ready" : "degraded",
                "not-checked",
                printerStatus.State,
                options.QueueName,
                $"{options.LabelWidthMm}x{options.LabelHeightMm}",
                printerStatus.Reason,
                printerStatus.QueuedJobs), statusCode: printerStatus.Ready ? 200 : 503);
        });

        api.MapPost("/test-print-jobs", async (
            TestPrintJobRequest request,
            TestPrintQueueService queue,
            IBarcodePrintability printability,
            IOptions<PrinterOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (request.EffectiveRequestId.Length is < 8 or > 128 ||
                !RequestIdPattern().IsMatch(request.EffectiveRequestId))
                return Results.Problem(statusCode: 400, title: "requestId is invalid.");
            if (request.EffectiveBarcode.Length is < 1 or > 14 ||
                !NumericBarcodePattern().IsMatch(request.EffectiveBarcode))
                return Results.Problem(statusCode: 400, title: "barcode must contain 1 to 14 digits.");

            var maximumTestCopies = Math.Min(options.Value.MaximumCopies, 5);
            if (request.Copies < 1 || request.Copies > maximumTestCopies)
                return Results.Problem(statusCode: 400, title: $"copies must be between 1 and {maximumTestCopies}.");

            var barcode = printability.Analyze(request.EffectiveBarcode);
            if (!barcode.Printable)
                return Results.Problem(
                    statusCode: 422,
                    title: "Barcode cannot be printed on a 38 mm label.",
                    detail: barcode.Reason);

            var submitted = await queue.SubmitAsync(request, cancellationToken);
            if (submitted.Conflict)
                return Results.Conflict(new ProblemDetails
                {
                    Status = 409,
                    Title = "requestId was already used with a different test print payload."
                });
            if (submitted.Busy)
                return Results.Problem(statusCode: 429, title: "Test print queue is full. Retry shortly with a new requestId.");
            if (submitted.Job.Status == "failed")
                return Results.Json(submitted.Job, statusCode: 503);
            return Results.Accepted($"/api/v1/test-print-jobs/{submitted.Job.JobId}", submitted.Job);
        }).RequireRateLimiting("print");

        api.MapGet("/test-print-jobs/{jobId}", (string jobId, TestPrintJobRegistry registry) =>
        {
            var job = registry.Get(jobId);
            return job is null
                ? Results.Problem(statusCode: 404, title: "Test print job was not found.")
                : Results.Ok(job);
        });

        return api;
    }

    [GeneratedRegex("^[A-Za-z0-9_.:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RequestIdPattern();

    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericBarcodePattern();
}
