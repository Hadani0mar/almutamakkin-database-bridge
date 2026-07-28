using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Jobs;
using Almutamakkin.BarcodeAgent.Models;
using Almutamakkin.BarcodeAgent.Printing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Tests;

public sealed class TestPrintQueueServiceTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "barcode-agent-test-print-queue",
        Guid.NewGuid().ToString("N"));
    private readonly CapturingLabelRenderer _renderer = new();
    private readonly CapturingRawPrinter _printer = new();
    private TestPrintQueueService? _service;

    [Fact]
    public async Task SubmitAsync_PrintsInMemoryTestLabelOnceWithoutProductRepository()
    {
        var request = new TestPrintJobRequest("test-request-789", "12345678", 2);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = await _service!.SubmitAsync(request, timeout.Token);
        var duplicate = await _service.SubmitAsync(request, timeout.Token);

        Assert.False(first.Conflict);
        Assert.False(first.Busy);
        Assert.Equal("submitted", first.Job.Status);
        Assert.Equal("12345678", first.Job.Barcode);
        Assert.Equal(73, first.Job.WindowsJobId);
        Assert.Equal(first.Job.JobId, duplicate.Job.JobId);
        Assert.Equal(1, _printer.PrintCalls);
        Assert.Equal("ALMUTAMAKKIN TEST", _renderer.BusinessName);
        Assert.Equal("PRINTER TEST", _renderer.Product?.Name);
        Assert.Equal("12345678", _renderer.Product?.Barcode);
        Assert.Equal(0L, _renderer.Product?.BarId);
        Assert.Equal(2, _renderer.Copies);
        Assert.Contains("12345678", _printer.DocumentName);
        Assert.Equal([1, 2, 3], _printer.Payload);
    }

    [Fact]
    public async Task SubmitAsync_SameRequestIdWithDifferentBarcodeDoesNotPrintAgain()
    {
        await _service!.SubmitAsync(
            new TestPrintJobRequest("test-request-conflict", "12345678", 1),
            CancellationToken.None);

        var conflict = await _service.SubmitAsync(
            new TestPrintJobRequest("test-request-conflict", "87654321", 1),
            CancellationToken.None);

        Assert.True(conflict.Conflict);
        Assert.Equal(1, _printer.PrintCalls);
    }

    public async Task InitializeAsync()
    {
        var options = Options.Create(new PrinterOptions
        {
            QueueName = "Test Printer",
            MaximumCopies = 20,
            QueueCapacity = 5
        });
        var registry = new TestPrintJobRegistry(Options.Create(new JobStoreOptions
        {
            DataDirectory = _directory,
            RetentionHours = 24
        }));
        _service = new TestPrintQueueService(
            registry,
            _renderer,
            _printer,
            options,
            NullLogger<TestPrintQueueService>.Instance);
        await _service.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_service is not null)
            await _service.StopAsync(CancellationToken.None);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class CapturingLabelRenderer : ILabelRenderer
    {
        public string? BusinessName { get; private set; }
        public ProductDto? Product { get; private set; }
        public int Copies { get; private set; }

        public byte[] Render(string businessName, ProductDto product, int copies)
        {
            BusinessName = businessName;
            Product = product;
            Copies = copies;
            return [1, 2, 3];
        }
    }

    private sealed class CapturingRawPrinter : IRawPrinter
    {
        public int PrintCalls { get; private set; }
        public string DocumentName { get; private set; } = string.Empty;
        public byte[] Payload { get; private set; } = [];

        public PrinterQueueStatus GetStatus() => new(true, "ready", null, 0, 0);

        public int Print(string documentName, ReadOnlySpan<byte> data)
        {
            PrintCalls++;
            DocumentName = documentName;
            Payload = data.ToArray();
            return 73;
        }
    }
}
