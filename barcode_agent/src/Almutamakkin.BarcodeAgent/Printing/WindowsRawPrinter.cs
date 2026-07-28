using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Almutamakkin.BarcodeAgent.Configuration;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Printing;

public sealed class WindowsRawPrinter(IOptions<PrinterOptions> options) : IRawPrinter
{
    private readonly string _queueName = options.Value.QueueName;

    public PrinterQueueStatus GetStatus()
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "unsupported", "RAW printing requires Windows.", 0, 0);
        if (!OpenPrinter(_queueName, out var printer, IntPtr.Zero))
            return new(false, "missing", new Win32Exception(Marshal.GetLastWin32Error()).Message, 0, 0);
        try
        {
            if (!TryReadPrinterInfo(printer, out var info, out var reason))
                return new(false, "unknown", reason, 0, 0);
            return EvaluateStatus(info.Status, checked((int)info.JobCount));
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    public int Print(string documentName, ReadOnlySpan<byte> data)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("RAW printing requires Windows.");
        var queueStatus = GetStatus();
        if (!queueStatus.Ready) throw new InvalidOperationException(queueStatus.Reason ?? "Printer queue is not ready.");
        if (!OpenPrinter(_queueName, out var printer, IntPtr.Zero)) ThrowLastWin32("OpenPrinter");

        var failures = new List<Exception>();
        var documentStarted = false;
        var pageStarted = false;
        uint jobId = 0;
        try
        {
            var docInfo = new DocInfo { DocumentName = documentName, DataType = "RAW" };
            jobId = StartDocPrinter(printer, 1, ref docInfo);
            if (jobId == 0) ThrowLastWin32("StartDocPrinter");
            documentStarted = true;
            if (!StartPagePrinter(printer)) ThrowLastWin32("StartPagePrinter");
            pageStarted = true;

            var bytes = data.ToArray();
            var unmanaged = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, unmanaged, bytes.Length);
                WriteAll(bytes.Length, (offset, remaining) =>
                {
                    if (!WritePrinter(printer, IntPtr.Add(unmanaged, offset), remaining, out var written))
                        ThrowLastWin32("WritePrinter");
                    return written;
                });
            }
            finally
            {
                Marshal.FreeCoTaskMem(unmanaged);
            }

            pageStarted = false;
            if (!EndPagePrinter(printer)) ThrowLastWin32("EndPagePrinter");
            documentStarted = false;
            if (!EndDocPrinter(printer)) ThrowLastWin32("EndDocPrinter");
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            if (pageStarted)
            {
                try { if (!EndPagePrinter(printer)) ThrowLastWin32("EndPagePrinter cleanup"); }
                catch (Exception exception) { failures.Add(exception); }
            }
            if (documentStarted)
            {
                try { if (!EndDocPrinter(printer)) ThrowLastWin32("EndDocPrinter cleanup"); }
                catch (Exception exception) { failures.Add(exception); }
            }
            try { if (!ClosePrinter(printer)) ThrowLastWin32("ClosePrinter"); }
            catch (Exception exception) { failures.Add(exception); }
        }

        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("RAW printing failed and cleanup also reported errors.", failures);
        return checked((int)jobId);
    }

    public static void WriteAll(int totalBytes, Func<int, int, int> writeChunk)
    {
        var offset = 0;
        while (offset < totalBytes)
        {
            var written = writeChunk(offset, totalBytes - offset);
            if (written <= 0 || written > totalBytes - offset)
                throw new IOException("WritePrinter returned an invalid byte count.");
            offset += written;
        }
    }

    public static PrinterQueueStatus EvaluateStatus(uint status, int queuedJobs)
    {
        var blocking = new (uint Flag, string Reason)[]
        {
            (0x00000001, "Printer queue is paused."),
            (0x00000002, "Printer reports an error."),
            (0x00000004, "Printer queue is pending deletion."),
            (0x00000008, "Printer reports a paper jam."),
            (0x00000010, "Printer is out of labels."),
            (0x00000040, "Printer reports a paper problem."),
            (0x00000080, "Printer is offline."),
            (0x00000800, "Printer output bin is full."),
            (0x00001000, "Printer is not available."),
            (0x00040000, "Printer is out of toner/media."),
            (0x00100000, "Printer requires user intervention."),
            (0x00200000, "Printer is out of memory."),
            (0x00400000, "Printer door is open."),
            (0x00800000, "Print server status is unknown.")
        };
        var issue = blocking.FirstOrDefault(item => (status & item.Flag) != 0);
        if (issue.Flag != 0) return new(false, "unavailable", issue.Reason, status, queuedJobs);
        var state = (status & 0x00000400) != 0 ? "printing" : (status & 0x00000200) != 0 ? "busy" : "ready";
        return new(true, state, null, status, queuedJobs);
    }

    private static bool TryReadPrinterInfo(IntPtr printer, out PrinterInfo2 info, out string? reason)
    {
        info = default;
        GetPrinter(printer, 2, IntPtr.Zero, 0, out var needed);
        if (needed == 0)
        {
            reason = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }
        var buffer = Marshal.AllocHGlobal(checked((int)needed));
        try
        {
            if (!GetPrinter(printer, 2, buffer, needed, out _))
            {
                reason = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }
            info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
            reason = null;
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void ThrowLastWin32(string operation) =>
        throw new Win32Exception(Marshal.GetLastWin32Error(), $"{operation} failed.");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo2
    {
        public IntPtr ServerName, PrinterName, ShareName, PortName, DriverName, Comment, Location;
        public IntPtr DevMode, SeparatorFile, PrintProcessor, DataType, Parameters, SecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, JobCount, AveragePagesPerMinute;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);
    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClosePrinter(IntPtr printer);
    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPrinter(IntPtr printer, uint level, IntPtr buffer, uint size, out uint needed);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint StartDocPrinter(IntPtr printer, int level, ref DocInfo docInfo);
    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EndDocPrinter(IntPtr printer);
    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartPagePrinter(IntPtr printer);
    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EndPagePrinter(IntPtr printer);
    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WritePrinter(IntPtr printer, IntPtr bytes, int count, out int written);
}
