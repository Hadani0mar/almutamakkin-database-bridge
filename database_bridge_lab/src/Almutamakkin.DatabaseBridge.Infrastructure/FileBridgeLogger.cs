using System.Globalization;
using System.Text;
using Almutamakkin.DatabaseBridge.Core;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class FileBridgeLogger : IBridgeLogger, IDisposable
{
    private readonly object _sync = new();
    private readonly IReadOnlyList<string> _logDirectories;
    private bool _disposed;

    public FileBridgeLogger()
    {
        _logDirectories = LabPaths.GetLogDirectories();
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warning(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", message, exception);

    public void Dispose()
    {
        _disposed = true;
    }

    private void Write(string level, string message, Exception? exception)
    {
        if (_disposed)
        {
            return;
        }

        var sanitizedMessage = SensitiveDataSanitizer.Sanitize(message);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var builder = new StringBuilder()
            .Append('[').Append(timestamp).Append(" UTC] ")
            .Append('[').Append(level).Append("] ")
            .AppendLine(sanitizedMessage);

        if (exception is not null)
        {
            builder.AppendLine(SensitiveDataSanitizer.Sanitize(exception.Message));

            if (exception.StackTrace is not null)
            {
                builder.AppendLine(exception.StackTrace);
            }
        }

        var entry = builder.ToString();
        var fileName = LabPaths.GetDailyLogFileName();

        lock (_sync)
        {
            foreach (var directory in _logDirectories)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, fileName), entry, Encoding.UTF8);
            }
        }
    }
}
