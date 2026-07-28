using System.Text.Json;
using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Models;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Jobs;

/// <summary>
/// Persists test-print idempotency separately from product print jobs.
/// Test jobs never contain a database identifier and cannot collide with normal print requests.
/// </summary>
public sealed class TestPrintJobRegistry
{
    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly TimeSpan _retention;
    private readonly Dictionary<string, PrintJobState> _byJobId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _jobIdByRequestId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<PrintJobResponse>> _completions = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public TestPrintJobRegistry(IOptions<JobStoreOptions> options)
    {
        var configured = options.Value.DataDirectory;
        var directory = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "test-print-jobs.json");
        _retention = TimeSpan.FromHours(options.Value.RetentionHours);
        Load();
    }

    public CreateJobResult GetOrCreate(string requestId, string barcode, int copies)
    {
        lock (_gate)
        {
            PruneExpiredUnsafe();
            if (_jobIdByRequestId.TryGetValue(requestId, out var existingJobId))
            {
                var existing = _byJobId[existingJobId];
                var conflict = !string.Equals(existing.Barcode, barcode, StringComparison.Ordinal) ||
                               existing.Copies != copies;
                return new CreateJobResult(existing.ToResponse(), false, conflict);
            }

            var state = new PrintJobState
            {
                JobId = Guid.NewGuid().ToString("N"),
                RequestId = requestId,
                Barcode = barcode,
                Copies = copies,
                Status = "queued",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _byJobId[state.JobId] = state;
            _jobIdByRequestId[requestId] = state.JobId;
            _completions[state.JobId] = NewCompletion();
            SaveUnsafe();
            return new CreateJobResult(state.ToResponse(), true, false);
        }
    }

    public PrintJobState? GetState(string jobId)
    {
        lock (_gate) return _byJobId.TryGetValue(jobId, out var value) ? value.Copy() : null;
    }

    public PrintJobResponse? Get(string jobId)
    {
        lock (_gate) return _byJobId.TryGetValue(jobId, out var value) ? value.ToResponse() : null;
    }

    public PrintJobResponse Update(
        string jobId,
        string status,
        int? windowsJobId = null,
        string? error = null)
    {
        lock (_gate)
        {
            var state = _byJobId[jobId];
            state.Status = status;
            state.WindowsJobId = windowsJobId ?? state.WindowsJobId;
            state.Error = error;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            SaveUnsafe();
            var response = state.ToResponse();
            if (status is "submitted" or "failed")
                _completions.GetValueOrDefault(jobId)?.TrySetResult(response);
            return response;
        }
    }

    public Task<PrintJobResponse> WaitForTerminalAsync(string jobId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var current = _byJobId[jobId].ToResponse();
            if (current.Status is "submitted" or "failed") return Task.FromResult(current);
            var completion = _completions.GetValueOrDefault(jobId) ?? NewCompletion();
            _completions[jobId] = completion;
            return completion.Task.WaitAsync(cancellationToken);
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var states = JsonSerializer.Deserialize<List<PrintJobState>>(File.ReadAllText(_filePath), _json) ?? [];
            foreach (var state in states.Where(item => DateTimeOffset.UtcNow - item.UpdatedAtUtc <= _retention))
            {
                if (state.Status is "queued" or "preparing")
                {
                    state.Status = "failed";
                    state.Error = "Agent restarted before this test job reached the Windows spooler.";
                    state.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
                _byJobId[state.JobId] = state;
                _jobIdByRequestId[state.RequestId] = state.JobId;
            }
            SaveUnsafe();
        }
        catch (Exception)
        {
            var damaged = _filePath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Move(_filePath, damaged, true);
        }
    }

    private void PruneExpiredUnsafe()
    {
        var cutoff = DateTimeOffset.UtcNow - _retention;
        foreach (var state in _byJobId.Values.Where(item => item.UpdatedAtUtc < cutoff).ToArray())
        {
            _byJobId.Remove(state.JobId);
            _jobIdByRequestId.Remove(state.RequestId);
            _completions.Remove(state.JobId);
        }
    }

    private void SaveUnsafe()
    {
        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_byJobId.Values, _json));
        File.Move(temporary, _filePath, true);
    }

    private static TaskCompletionSource<PrintJobResponse> NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
