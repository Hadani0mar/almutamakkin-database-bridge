namespace Almutamakkin.DatabaseBridge.Protocol;

public static class MessageTypes
{
    public const string BridgeHealth = "bridge.health";
    public const string DatabaseTest = "database.test";
    public const string DatabaseList = "database.list";
    public const string SqlExecute = "sql.execute";
    /// <summary>
    /// Executes a server-owned, signed query package.  The mobile device sends
    /// only the package id and typed values; SQL never crosses the phone tunnel.
    /// </summary>
    public const string QueryPackageExecute = "query.execute";
    public const string MarketingProductMovement = "marketing.product_movement";
    public const string InfinityProductMovement = "infinity.product_movement";

    public const string PrinterHealth = "printer.health";
    public const string PrinterProductsSearch = "printer.products.search";
    public const string PrinterProductsByBarcode = "printer.products.byBarcode";
    public const string PrinterProductsByBarId = "printer.products.byBarId";
    public const string PrinterPrintSubmit = "printer.print.submit";
    public const string PrinterTestSubmit = "printer.test.submit";

    public const string ProductPhoto = "product.photo";

    /// <summary>
    /// Converts uploaded image bytes to GIF89a and MERGEs into
    /// Inventory.Data_ProductPhotos for Infinity only. Gated by
    /// EnableInfinityProductPhotoWrite.
    /// </summary>
    public const string ProductPhotoUpsert = "product.photo.upsert";

    /// <summary>
    /// Phase 0/1 change-stream foundation: phone probes whether a domain's
    /// local revision has moved past its last known cursor. Cheap, no SQL.
    /// </summary>
    public const string ChangesProbe = "changes.probe";

    /// <summary>
    /// Phase 0/1 change-stream foundation: phone pulls the current
    /// revision/watermark for one or more domains. Stub until cloud ticket
    /// publish (Supabase) ships; returns local cursor status only.
    /// </summary>
    public const string ChangesPull = "changes.pull";

    public const string BridgeHealthResponse = "bridge.health.response";
    public const string DatabaseTestResponse = "database.test.response";
    public const string DatabaseListResponse = "database.list.response";
    public const string SqlExecuteResponse = "sql.execute.response";
    public const string QueryPackageExecuteResponse = "query.execute.response";
    public const string MarketingProductMovementResponse = "marketing.product_movement.response";
    public const string InfinityProductMovementResponse = "infinity.product_movement.response";

    public const string PrinterHealthResponse = "printer.health.response";
    public const string PrinterProductsSearchResponse = "printer.products.search.response";
    public const string PrinterProductsByBarcodeResponse = "printer.products.byBarcode.response";
    public const string PrinterProductsByBarIdResponse = "printer.products.byBarId.response";
    public const string PrinterPrintSubmitResponse = "printer.print.submit.response";
    public const string PrinterTestSubmitResponse = "printer.test.submit.response";

    public const string ProductPhotoResponse = "product.photo.response";
    public const string ProductPhotoUpsertResponse = "product.photo.upsert.response";

    public const string ChangesProbeResponse = "changes.probe.response";
    public const string ChangesPullResponse = "changes.pull.response";

    public static readonly IReadOnlySet<string> Commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        BridgeHealth,
        DatabaseTest,
        DatabaseList,
        SqlExecute,
        QueryPackageExecute,
        MarketingProductMovement,
        InfinityProductMovement,
        PrinterHealth,
        PrinterProductsSearch,
        PrinterProductsByBarcode,
        PrinterProductsByBarId,
        PrinterPrintSubmit,
        PrinterTestSubmit,
        ProductPhoto,
        ProductPhotoUpsert,
        ChangesProbe,
        ChangesPull,
    };

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        BridgeHealth,
        DatabaseTest,
        DatabaseList,
        SqlExecute,
        QueryPackageExecute,
        PrinterHealth,
        PrinterProductsSearch,
        PrinterProductsByBarcode,
        PrinterProductsByBarId,
        PrinterPrintSubmit,
        PrinterTestSubmit,
        ProductPhoto,
        ProductPhotoUpsert,
        ChangesProbe,
        ChangesPull,
        BridgeHealthResponse,
        DatabaseTestResponse,
        DatabaseListResponse,
        SqlExecuteResponse,
        QueryPackageExecuteResponse,
        MarketingProductMovementResponse,
        InfinityProductMovementResponse,
        PrinterHealthResponse,
        PrinterProductsSearchResponse,
        PrinterProductsByBarcodeResponse,
        PrinterProductsByBarIdResponse,
        PrinterPrintSubmitResponse,
        PrinterTestSubmitResponse,
        ProductPhotoResponse,
        ProductPhotoUpsertResponse,
        ChangesProbeResponse,
        ChangesPullResponse,
    };

    public static string ToResponseType(string commandMessageType) =>
        commandMessageType.ToLowerInvariant() switch
        {
            BridgeHealth => BridgeHealthResponse,
            DatabaseTest => DatabaseTestResponse,
            DatabaseList => DatabaseListResponse,
            SqlExecute => SqlExecuteResponse,
            QueryPackageExecute => QueryPackageExecuteResponse,
            MarketingProductMovement => MarketingProductMovementResponse,
            InfinityProductMovement => InfinityProductMovementResponse,
            PrinterHealth => PrinterHealthResponse,
            PrinterProductsSearch => PrinterProductsSearchResponse,
            PrinterProductsByBarcode => PrinterProductsByBarcodeResponse,
            PrinterProductsByBarId => PrinterProductsByBarIdResponse,
            PrinterPrintSubmit => PrinterPrintSubmitResponse,
            PrinterTestSubmit => PrinterTestSubmitResponse,
            ProductPhoto => ProductPhotoResponse,
            ProductPhotoUpsert => ProductPhotoUpsertResponse,
            ChangesProbe => ChangesProbeResponse,
            ChangesPull => ChangesPullResponse,
            _ => $"{commandMessageType}.response",
        };
}
