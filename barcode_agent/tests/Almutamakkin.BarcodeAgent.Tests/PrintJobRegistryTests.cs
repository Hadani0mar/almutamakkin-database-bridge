using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Jobs;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Tests;

public sealed class PrintJobRegistryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "barcode-agent-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetOrCreate_IsIdempotentAndPersistsAcrossRestart()
    {
        var registry = CreateRegistry();
        var first = registry.GetOrCreate("request-123", 91, 2);
        var duplicate = registry.GetOrCreate("request-123", 91, 2);
        var conflict = registry.GetOrCreate("request-123", 92, 2);

        Assert.True(first.Created);
        Assert.False(duplicate.Created);
        Assert.Equal(first.Job.JobId, duplicate.Job.JobId);
        Assert.True(conflict.Conflict);

        registry.Update(first.Job.JobId, "submitted", windowsJobId: 44);
        var reloaded = CreateRegistry().GetOrCreate("request-123", 91, 2);
        Assert.False(reloaded.Created);
        Assert.Equal(44, reloaded.Job.WindowsJobId);
    }

    private PrintJobRegistry CreateRegistry() => new(Options.Create(new JobStoreOptions
    {
        DataDirectory = _directory,
        RetentionHours = 24
    }));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
