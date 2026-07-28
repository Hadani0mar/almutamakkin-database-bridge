using System.Data.SqlClient;
using System.Drawing.Printing;
using System.Net;
using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Printing;
using Almutamakkin.BarcodeBridge.Configuration;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeBridge.Diagnostics;

public static class BridgeDiagnostics
{
    public static async Task<DatabaseTestResult> TestDatabaseAsync(
        BridgeSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.SqlServer) ||
            string.IsNullOrWhiteSpace(settings.Database) ||
            string.IsNullOrWhiteSpace(settings.Username) ||
            string.IsNullOrWhiteSpace(settings.Password))
            return new DatabaseTestResult(false, null, "أكمل عنوان القاعدة واسمها والمستخدم وكلمة المرور أولاً.");
        try
        {
            await using var connection = new SqlConnection(settings.BuildConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT TOP 1 LTRIM(RTRIM(A_NAME)) FROM SITTEINGS WHERE A_NAME IS NOT NULL",
                connection)
            {
                CommandTimeout = 10
            };
            var businessName = (await command.ExecuteScalarAsync(cancellationToken))?.ToString()?.Trim();
            return new DatabaseTestResult(true, businessName, null);
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or ArgumentException)
        {
            return new DatabaseTestResult(false, null, Sanitize(exception.Message));
        }
    }

    public static PrinterTestResult TestPrinter(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return new PrinterTestResult(false, "missing", "لم يتم اختيار طابعة.", 0);
        var installed = PrinterSettings.InstalledPrinters.Cast<string>()
            .Any(name => string.Equals(name, printerName, StringComparison.OrdinalIgnoreCase));
        if (!installed)
            return new PrinterTestResult(false, "missing", "الطابعة المختارة غير مثبتة على هذا الجهاز.", 0);

        var printer = new WindowsRawPrinter(Options.Create(new PrinterOptions { QueueName = printerName }));
        var status = printer.GetStatus();
        return new PrinterTestResult(status.Ready, status.State, status.Reason, status.QueuedJobs);
    }

    public static IReadOnlyList<string> InstalledPrinters() =>
        PrinterSettings.InstalledPrinters.Cast<string>()
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static string Sanitize(string message)
    {
        var line = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "خطأ غير معروف.";
        return line.Length <= 300 ? line : line[..300];
    }
}

public sealed record DatabaseTestResult(bool Ready, string? BusinessName, string? Error);
public sealed record PrinterTestResult(bool Ready, string State, string? Reason, int QueuedJobs);

public sealed record DiagnosticsReport(
    bool SettingsValid,
    string[] SettingsErrors,
    string? LanIp,
    bool DatabaseReady,
    string? BusinessName,
    string? DatabaseError,
    bool PrinterReady,
    string PrinterState,
    string? PrinterReason,
    string PrinterName,
    int Port);
