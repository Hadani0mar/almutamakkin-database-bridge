namespace Almutamakkin.BarcodeAgent.Printing;

public interface IRawPrinter
{
    PrinterQueueStatus GetStatus();
    int Print(string documentName, ReadOnlySpan<byte> data);
}

public sealed record PrinterQueueStatus(
    bool Ready,
    string State,
    string? Reason,
    uint NativeStatus,
    int QueuedJobs);
