using System.Text.Json;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class PrinterHealthHandler(IPrinterBridgeFacade printer) : ICommandHandler
{
    public string MessageType => MessageTypes.PrinterHealth;

    public async Task<BridgeResponse> HandleAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        var result = await printer.HealthAsync(cancellationToken);
        return Map(command, result);
    }

    internal static BridgeResponse Map(BridgeCommand command, PrinterBridgeOperationResult result) =>
        result.Success
            ? BridgeResponseBuilder.Success(command, result.Payload)
            : BridgeResponseBuilder.Failure(
                command,
                result.ErrorCode ?? ErrorCodes.InternalError,
                result.ErrorMessage ?? "فشل أمر الطابعة.",
                retryable: result.Retryable);
}

public sealed class PrinterProductsSearchHandler(IPrinterBridgeFacade printer) : ICommandHandler
{
    public string MessageType => MessageTypes.PrinterProductsSearch;

    public async Task<BridgeResponse> HandleAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        var query = ReadString(command.Payload, "query") ?? ReadString(command.Payload, "q") ?? string.Empty;
        var limit = ReadInt(command.Payload, "limit") ?? 20;
        var result = await printer.SearchProductsAsync(query, limit, cancellationToken);
        return PrinterHealthHandler.Map(command, result);
    }

    private static string? ReadString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (!payload.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? ReadInt(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (!payload.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }
}

public sealed class PrinterProductsByBarcodeHandler(IPrinterBridgeFacade printer) : ICommandHandler
{
    public string MessageType => MessageTypes.PrinterProductsByBarcode;

    public async Task<BridgeResponse> HandleAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        var barcode = command.Payload.ValueKind == JsonValueKind.Object
            && command.Payload.TryGetProperty("barcode", out var value)
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
        var result = await printer.GetProductsByBarcodeAsync(barcode, cancellationToken);
        return PrinterHealthHandler.Map(command, result);
    }
}

public sealed class PrinterProductsByBarIdHandler(IPrinterBridgeFacade printer) : ICommandHandler
{
    public string MessageType => MessageTypes.PrinterProductsByBarId;

    public async Task<BridgeResponse> HandleAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        long barId = 0;
        if (command.Payload.ValueKind == JsonValueKind.Object
            && command.Payload.TryGetProperty("barId", out var value))
        {
            if (value.ValueKind == JsonValueKind.Number) value.TryGetInt64(out barId);
            else long.TryParse(value.GetString(), out barId);
        }

        var result = await printer.GetProductByBarIdAsync(barId, cancellationToken);
        return PrinterHealthHandler.Map(command, result);
    }
}

public sealed class PrinterPrintSubmitHandler(IPrinterBridgeFacade printer) : ICommandHandler
{
    public string MessageType => MessageTypes.PrinterPrintSubmit;

    public async Task<BridgeResponse> HandleAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        var requestId = ReadString(command.Payload, "requestId") ?? command.RequestId;
        long barId = 0;
        var copies = 1;
        if (command.Payload.ValueKind == JsonValueKind.Object)
        {
            if (command.Payload.TryGetProperty("barId", out var barIdEl))
            {
                if (barIdEl.ValueKind == JsonValueKind.Number) barIdEl.TryGetInt64(out barId);
                else long.TryParse(barIdEl.GetString(), out barId);
            }

            if (command.Payload.TryGetProperty("copies", out var copiesEl))
            {
                if (copiesEl.ValueKind == JsonValueKind.Number) copiesEl.TryGetInt32(out copies);
                else int.TryParse(copiesEl.GetString(), out copies);
            }
        }

        var result = await printer.SubmitPrintAsync(requestId, barId, copies, cancellationToken);
        return PrinterHealthHandler.Map(command, result);
    }

    private static string? ReadString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (!payload.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}

public sealed class PrinterTestSubmitHandler(IPrinterBridgeFacade printer) : ICommandHandler
{
    public string MessageType => MessageTypes.PrinterTestSubmit;

    public async Task<BridgeResponse> HandleAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        var requestId = command.RequestId;
        var barcode = string.Empty;
        var copies = 1;
        if (command.Payload.ValueKind == JsonValueKind.Object)
        {
            if (command.Payload.TryGetProperty("requestId", out var requestEl)
                && requestEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(requestEl.GetString()))
            {
                requestId = requestEl.GetString()!;
            }

            if (command.Payload.TryGetProperty("barcode", out var barcodeEl))
            {
                barcode = barcodeEl.GetString()?.Trim() ?? string.Empty;
            }

            if (command.Payload.TryGetProperty("copies", out var copiesEl))
            {
                if (copiesEl.ValueKind == JsonValueKind.Number) copiesEl.TryGetInt32(out copies);
                else int.TryParse(copiesEl.GetString(), out copies);
            }
        }

        var result = await printer.SubmitTestPrintAsync(requestId, barcode, copies, cancellationToken);
        return PrinterHealthHandler.Map(command, result);
    }
}
