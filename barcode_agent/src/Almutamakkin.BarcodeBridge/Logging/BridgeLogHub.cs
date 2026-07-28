using Microsoft.Extensions.Logging;

namespace Almutamakkin.BarcodeBridge.Logging;

public sealed class BridgeLogHub
{
    private readonly object _gate = new();
    private readonly Queue<BridgeLogEntry> _entries = new();
    private const int Capacity = 1000;

    public event Action<BridgeLogEntry>? EntryAdded;

    public IReadOnlyList<BridgeLogEntry> Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }

    public void Add(LogLevel level, string message, Exception? exception = null)
    {
        var clean = exception is null ? message : $"{message} — {exception.Message}";
        var entry = new BridgeLogEntry(DateTimeOffset.Now, level, clean);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity) _entries.Dequeue();
        }
        EntryAdded?.Invoke(entry);
    }
}

public sealed record BridgeLogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message)
{
    public override string ToString() => $"{Timestamp:HH:mm:ss}  {Level,-11}  {Message}";
}

public sealed class BridgeLoggerProvider(BridgeLogHub hub) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new BridgeLogger(categoryName, hub);
    public void Dispose() { }

    private sealed class BridgeLogger(string category, BridgeLogHub hub) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var shortCategory = category.Split('.').LastOrDefault() ?? category;
            hub.Add(logLevel, $"[{shortCategory}] {formatter(state, exception)}", exception);
        }
    }
}
