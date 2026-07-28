using System.Text.Json.Serialization;

namespace Almutamakkin.BarcodeAgent.Models;

public sealed record ProductDto(
    long BarId,
    long ItemId,
    string Name,
    string Barcode,
    decimal Quantity,
    decimal SalePrice,
    int? UnitId,
    string UnitName,
    decimal UnitQty,
    decimal LastPurchasePrice,
    string? LastSupplier,
    bool Printable,
    string? PrintabilityReason);

public sealed record PrintJobRequest(
    string? RequestId,
    long BarId,
    int Copies,
    string? IdempotencyKey = null)
{
    [JsonIgnore]
    public string EffectiveRequestId =>
        !string.IsNullOrWhiteSpace(RequestId) ? RequestId.Trim() : IdempotencyKey?.Trim() ?? string.Empty;
}

public sealed record TestPrintJobRequest(
    string? RequestId,
    string? Barcode,
    int Copies)
{
    [JsonIgnore]
    public string EffectiveRequestId => RequestId?.Trim() ?? string.Empty;

    [JsonIgnore]
    public string EffectiveBarcode => Barcode?.Trim() ?? string.Empty;
}

public sealed record PrintJobResponse(
    string JobId,
    string RequestId,
    string Status,
    long BarId,
    long ItemId,
    int Copies,
    string Barcode,
    int? WindowsJobId,
    string? Error,
    DateTimeOffset UpdatedAtUtc);

public sealed record HealthResponse(
    string Status,
    string Database,
    string Printer,
    string PrinterQueue,
    string LabelSize,
    string? PrinterReason = null,
    int QueuedWindowsJobs = 0);
