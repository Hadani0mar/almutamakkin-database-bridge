namespace Almutamakkin.DatabaseBridge.Core;

public sealed class PrinterBridgeOperationResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Retryable { get; init; }
    public object? Payload { get; init; }

    public static PrinterBridgeOperationResult Ok(object? payload) =>
        new() { Success = true, Payload = payload };

    public static PrinterBridgeOperationResult Fail(
        string errorCode,
        string errorMessage,
        bool retryable = false) =>
        new()
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Retryable = retryable,
        };
}

public interface IPrinterBridgeFacade
{
    Task<PrinterBridgeOperationResult> HealthAsync(CancellationToken cancellationToken);

    Task<PrinterBridgeOperationResult> SearchProductsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<PrinterBridgeOperationResult> GetProductsByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken);

    Task<PrinterBridgeOperationResult> GetProductByBarIdAsync(
        long barId,
        CancellationToken cancellationToken);

    Task<PrinterBridgeOperationResult> SubmitPrintAsync(
        string requestId,
        long barId,
        int copies,
        CancellationToken cancellationToken);

    Task<PrinterBridgeOperationResult> SubmitTestPrintAsync(
        string requestId,
        string barcode,
        int copies,
        CancellationToken cancellationToken);
}
