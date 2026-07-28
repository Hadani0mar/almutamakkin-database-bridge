using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Almutamakkin.BarcodeBridge.Configuration;
using Almutamakkin.BarcodeBridge.Diagnostics;
using Almutamakkin.BarcodeBridge.Logging;
using Almutamakkin.BarcodeBridge.Networking;
using Almutamakkin.BarcodeBridge.Server;
using Almutamakkin.BarcodeBridge.Windows;

namespace Almutamakkin.BarcodeBridge;

internal static class Program
{
    private const string MutexName = @"Local\Almutamakkin.BarcodeBridge";

    [STAThread]
    private static int Main(string[] args)
    {
        var firewallArgument = Array.FindIndex(args, value =>
            string.Equals(value, "--configure-firewall", StringComparison.OrdinalIgnoreCase));
        if (firewallArgument >= 0)
        {
            if (firewallArgument + 1 >= args.Length || !int.TryParse(args[firewallArgument + 1], out var firewallPort))
                return 2;
            return RunFirewallMode(firewallPort);
        }
        if (args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase))
            return RunDiagnosticsModeAsync().GetAwaiter().GetResult();

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("البرنامج يعمل بالفعل بجانب الساعة.", "جسر طباعة الباركود", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        var store = new EncryptedSettingsStore();
        var logs = new BridgeLogHub();
        var server = new BridgeServerController(logs, store.DataDirectory);
        Application.ThreadException += (_, eventArgs) =>
        {
            logs.Add(Microsoft.Extensions.Logging.LogLevel.Critical, "خطأ غير متوقع في الواجهة.", eventArgs.Exception);
            MessageBox.Show(eventArgs.Exception.Message, "خطأ غير متوقع", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        var startMinimized = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        Application.Run(new MainForm(store, logs, server, startMinimized));
        server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.KeepAlive(mutex);
        return 0;
    }

    private static int RunFirewallMode(int port)
    {
        try
        {
            return FirewallConfigurator.ConfigureElevated(port);
        }
        catch
        {
            return 2;
        }
    }

    private static async Task<int> RunDiagnosticsModeAsync()
    {
        ConsoleAttachment.AttachToParent();
        var store = new EncryptedSettingsStore();
        var settings = store.LoadOrCreate();
        var lan = LanIpDetector.Detect();
        var database = await BridgeDiagnostics.TestDatabaseAsync(settings);
        var printer = BridgeDiagnostics.TestPrinter(settings.PrinterName);
        var errors = settings.Validate().ToArray();
        var report = new DiagnosticsReport(
            errors.Length == 0,
            errors,
            lan?.ToString(),
            database.Ready,
            database.BusinessName,
            database.Error,
            printer.Ready,
            printer.State,
            printer.Reason,
            settings.PrinterName,
            settings.Port);
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));
        return report.SettingsValid && lan is not null && report.DatabaseReady && report.PrinterReady ? 0 : 1;
    }

}

internal static class ConsoleAttachment
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    public static void AttachToParent()
    {
        if (!AttachConsole(AttachParentProcess)) return;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
