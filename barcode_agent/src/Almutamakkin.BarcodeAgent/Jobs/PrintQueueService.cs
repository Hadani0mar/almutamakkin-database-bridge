using System.Threading.Channels;
using Almutamakkin.BarcodeAgent.Database;
using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Models;
using Almutamakkin.BarcodeAgent.Printing;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Jobs;

public sealed class PrintQueueService(
    PrintJobRegistry registry,
    IProductRepository products,
    ILabelRenderer renderer,
    IRawPrinter printer,
    IOptions<PrinterOptions> options,
    ILogger<PrintQueueService> logger) : BackgroundService
{
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(options.Value.QueueCapacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });

    public async Task<SubmitResult> SubmitAsync(PrintJobRequest request, CancellationToken cancellationToken)
    {
        var creation = registry.GetOrCreate(request.EffectiveRequestId, request.BarId, request.Copies);
        if (creation.Conflict) return new SubmitResult(creation.Job, true, false);
        if (creation.Created && !_queue.Writer.TryWrite(creation.Job.JobId))
        {
            var busy = registry.Update(creation.Job.JobId, "failed", error: "The print queue is full. Retry with a new requestId.");
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
                var product = await products.GetByBarIdAsync(state.BarId, stoppingToken)
                    ?? throw new InvalidOperationException("The selected BAR_ID no longer exists or has no printable barcode.");
                if (!product.Printable) throw new InvalidOperationException(product.PrintabilityReason ?? "Barcode is not printable.");
                var businessName = await products.GetBusinessNameAsync(stoppingToken);
                if (string.IsNullOrWhiteSpace(businessName))
                    throw new InvalidOperationException("SITTEINGS.A_NAME is empty.");
                var payload = renderer.Render(businessName, product, state.Copies);
                var windowsJobId = printer.Print($"Almutamakkin barcode {state.BarId} x{state.Copies}", payload);
                registry.Update(jobId, "submitted", product, windowsJobId);
                logger.LogInformation(
                    "Barcode print job {JobId} submitted to Windows as {WindowsJobId} for BAR_ID {BarId}",
                    jobId, windowsJobId, state.BarId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                registry.Update(jobId, "failed", error: "Agent stopped before the job was submitted.");
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Barcode print job {JobId} failed", jobId);
                registry.Update(jobId, "failed", error: "The print job could not be submitted. Check the agent logs.");
            }
        }
    }
}

public sealed record SubmitResult(PrintJobResponse Job, bool Conflict, bool Busy);
