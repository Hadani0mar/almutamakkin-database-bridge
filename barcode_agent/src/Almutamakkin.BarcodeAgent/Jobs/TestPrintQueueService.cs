using System.Threading.Channels;
using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Models;
using Almutamakkin.BarcodeAgent.Printing;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Jobs;

/// <summary>
/// Sends a self-contained test label to the configured Windows printer.
/// This service deliberately has no product repository or database dependency.
/// </summary>
public sealed class TestPrintQueueService(
    TestPrintJobRegistry registry,
    ILabelRenderer renderer,
    IRawPrinter printer,
    IOptions<PrinterOptions> options,
    ILogger<TestPrintQueueService> logger) : BackgroundService
{
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(options.Value.QueueCapacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });

    public async Task<SubmitResult> SubmitAsync(TestPrintJobRequest request, CancellationToken cancellationToken)
    {
        var creation = registry.GetOrCreate(request.EffectiveRequestId, request.EffectiveBarcode, request.Copies);
        if (creation.Conflict) return new SubmitResult(creation.Job, true, false);
        if (creation.Created && !_queue.Writer.TryWrite(creation.Job.JobId))
        {
            var busy = registry.Update(creation.Job.JobId, "failed", error: "The test print queue is full. Retry with a new requestId.");
            return new SubmitResult(busy, false, true);
        }
        var result = await registry.WaitForTerminalAsync(creation.Job.JobId, cancellationToken);
        return new SubmitResult(result, false, false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var state = registry.GetState(jobId);
            if (state is null) continue;
            try
            {
                registry.Update(jobId, "preparing");
                var testProduct = new ProductDto(
                    BarId: 0,
                    ItemId: 0,
                    Name: "PRINTER TEST",
                    Barcode: state.Barcode,
                    Quantity: 0,
                    SalePrice: 0,
                    UnitId: null,
                    UnitName: "N/A",
                    UnitQty: 0,
                    LastPurchasePrice: 0,
                    LastSupplier: null,
                    Printable: true,
                    PrintabilityReason: null);
                var payload = renderer.Render("ALMUTAMAKKIN TEST", testProduct, state.Copies);
                var windowsJobId = printer.Print($"Almutamakkin test barcode {state.Barcode} x{state.Copies}", payload);
                registry.Update(jobId, "submitted", windowsJobId);
                logger.LogInformation(
                    "Test barcode print job {JobId} submitted to Windows as {WindowsJobId}",
                    jobId, windowsJobId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                registry.Update(jobId, "failed", error: "Agent stopped before the test job was submitted.");
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Test barcode print job {JobId} failed", jobId);
                registry.Update(jobId, "failed", error: "The test print job could not be submitted. Check the agent logs.");
            }
        }
    }
}
