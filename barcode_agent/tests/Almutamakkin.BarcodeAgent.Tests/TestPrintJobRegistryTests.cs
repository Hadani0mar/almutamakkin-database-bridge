using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Jobs;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Tests;

public sealed class TestPrintJobRegistryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "barcode-agent-test-print-registry",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetOrCreate_IsIdempotentForSameBarcodeAndRejectsDifferentPayload()
    {
        var registry = CreateRegistry();

        var first = registry.GetOrCreate("test-request-123", "12345678", 2);
        var duplicate = registry.GetOrCreate("test-request-123", "12345678", 2);
        var barcodeConflict = registry.GetOrCreate("test-request-123", "87654321", 2);
        var copiesConflict = registry.GetOrCreate("test-request-123", "12345678", 3);

        Assert.True(first.Created);
        Assert.False(duplicate.Created);
        Assert.Equal(first.Job.JobId, duplicate.Job.JobId);
        Assert.True(barcodeConflict.Conflict);
        Assert.True(copiesConflict.Conflict);
    }

    [Fact]
    public void SubmittedTestJob_PersistsSeparatelyAcrossRestart()
    {
        var registry = CreateRegistry();
        var first = registry.GetOrCreate("test-request-456", "12345678", 1);
        registry.Update(first.Job.JobId, "submitted", windowsJobId: 81);

        var reloaded = CreateRegistry().GetOrCreate("test-request-456", "12345678", 1);

        Assert.False(reloaded.Created);
        Assert.Equal("submitted", reloaded.Job.Status);
        Assert.Equal("12345678", reloaded.Job.Barcode);
        Assert.Equal(0, reloaded.Job.BarId);
        Assert.Equal(0, reloaded.Job.ItemId);
        Assert.Equal(81, reloaded.Job.WindowsJobId);
        Assert.True(File.Exists(Path.Combine(_directory, "test-print-jobs.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "print-jobs.json")));
    }

    private TestPrintJobRegistry CreateRegistry() => new(Options.Create(new JobStoreOptions
    {
        DataDirectory = _directory,
        RetentionHours = 24
    }));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
