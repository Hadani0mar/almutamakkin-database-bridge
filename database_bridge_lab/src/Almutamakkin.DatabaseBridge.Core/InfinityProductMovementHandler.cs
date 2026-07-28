using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// Executes the fixed Infinity product-movement contract. It never accepts
/// mobile SQL and never routes an Infinity request through a Marketing profile.
/// </summary>
public sealed class InfinityProductMovementHandler : ICommandHandler
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly ILiveDatabaseProfileResolver _profileResolver;
    private readonly ISqlCommandExecutor _executor;
    private readonly IRequestValidator _validator;
    private readonly IBridgeLogger _logger;

    public InfinityProductMovementHandler(
        IDatabaseProfileStore profileStore,
        ILiveDatabaseProfileResolver profileResolver,
        ISqlCommandExecutor executor,
        IRequestValidator validator,
        IBridgeLogger logger)
    {
        _profileStore = profileStore;
        _profileResolver = profileResolver;
        _executor = executor;
        _validator = validator;
        _logger = logger;
    }

    public string MessageType => MessageTypes.InfinityProductMovement;

    public async Task<BridgeResponse> HandleAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        var payload = BridgeJson.DeserializeInfinityProductMovementPayload(command.Payload);
        var validation = payload is null
            ? RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "تعذر قراءة طلب حركة الصنف.")
            : _validator.ValidateInfinityProductMovementPayload(payload);
        if (!validation.IsValid)
        {
            return BridgeResponseBuilder.FromValidation(command, validation);
        }

        _profileStore.Reload();
        var profile = _profileResolver.Resolve("InfinityRetailDB");
        if (profile is null || !string.Equals(_profileResolver.GetSystem(profile), "infinity", StringComparison.OrdinalIgnoreCase))
        {
            return BridgeResponseBuilder.Failure(command, ErrorCodes.DatabaseProfileNotFound, "ملف قاعدة InfinityRetailDB غير متاح.");
        }

        var sqlRequest = new SqlExecutePayload
        {
            DatabaseProfile = "InfinityRetailDB",
            Sql = BuildQuery(payload!),
            TimeoutSeconds = 30,
            MaxRows = 1200,
            Parameters = new Dictionary<string, SqlParameterValue>
            {
                ["productId"] = new() { Type = "int", Value = payload!.ProductId },
                ["searchTerm"] = new() { Type = "string", Value = payload.SearchTerm?.Trim() ?? string.Empty },
                ["startDate"] = new() { Type = "datetime", Value = payload.StartDate.Date },
                ["endDate"] = new() { Type = "datetime", Value = payload.EndDate.Date },
                ["endExclusive"] = new() { Type = "datetime", Value = payload.EndDate.Date.AddDays(1) },
            },
        };

        _logger.Info($"Executing named Infinity product movement for '{payload.SearchTerm ?? payload.ProductId.ToString()}' ({payload.Granularity}).");
        var result = await _executor.ExecuteAsync(profile, sqlRequest, cancellationToken);
        if (!result.Success)
        {
            return BridgeResponseBuilder.Failure(command, result.ErrorCode ?? ErrorCodes.SqlExecutionFailed,
                result.ErrorMessage ?? "فشل جلب حركة الصنف.", retryable: result.ErrorCode == ErrorCodes.SqlTimeout);
        }

        return BridgeResponseBuilder.Success(command, new
        {
            system = "infinity",
            operation = "product_movement",
            productId = payload.ProductId,
            searchTerm = payload.SearchTerm,
            executionTimeMs = result.ExecutionTimeMs,
            wasTruncated = result.WasTruncated,
            rows = result.ResultSets.FirstOrDefault()?.Rows ?? [],
        });
    }

    private static string BuildQuery(InfinityProductMovementPayload payload)
    {
        const string soldQuantityExpr = "SUM(CASE WHEN movement.BaseQuantity > 0 THEN movement.BaseQuantity ELSE 0 END)";
        const string returnedQuantityExpr = "ABS(SUM(CASE WHEN movement.BaseQuantity < 0 THEN movement.BaseQuantity ELSE 0 END))";
        const string netQuantityExpr = "SUM(movement.BaseQuantity)";
        const string netAmountExpr = "SUM(movement.NetLineAmount)";
        const string discountExpr = "SUM(movement.DiscountAmount)";
        const string source = """
WITH MatchedProducts AS (
    SELECT DISTINCT barcode.ProductID_FK
    FROM Inventory.Data_View_ProductUOMBarcodes AS barcode
    WHERE @searchTerm <> N''
      AND barcode.ProductBarcode = @searchTerm

    UNION

    SELECT DISTINCT product.ProductID_PK
    FROM Inventory.Data_Products AS product
    WHERE @searchTerm <> N''
      AND (
          product.ProductName LIKE N'%' + @searchTerm + N'%'
          OR product.ProductShortName LIKE N'%' + @searchTerm + N'%'
          OR product.ProductCode = @searchTerm
      )

    UNION

    SELECT @productId
    WHERE @searchTerm = N'' AND @productId > 0
), SourceMovements AS (
    SELECT
        CAST(invoice.SalesInvoiceDate AS date) AS SaleDate,
        item.ProductID_FK AS ProductId,
        item.ProductCode AS ProductCode,
        item.ProductName AS ProductName,
        item.QYT * item.UnitBaseQYT AS BaseQuantity,
        item.SubTotal - ISNULL(item.DiscountAmount, 0) AS NetLineAmount,
        ISNULL(item.DiscountAmount, 0) AS DiscountAmount,
        invoice.SalesInvoiceID_PK AS SalesInvoiceId
    FROM SALES.Data_View_SalesInvoiceItems AS item
    INNER JOIN SALES.Data_View_SalesInvoices AS invoice
        ON invoice.SalesInvoiceID_PK = item.SalesInvoiceID_FK
    INNER JOIN MatchedProducts AS matched
        ON matched.ProductID_FK = item.ProductID_FK
    WHERE invoice.IsPosted = 1
      AND invoice.SalesInvoiceDate >= @startDate
      AND invoice.SalesInvoiceDate < @endExclusive
)
""";

        return payload.Granularity.ToLowerInvariant() switch
        {
            "daily" => $"""
SELECT movement.SaleDate AS periodStart,
       movement.SaleDate AS periodEnd,
       movement.ProductId AS productId,
       MAX(movement.ProductCode) AS productCode,
       MAX(movement.ProductName) AS productName,
       {soldQuantityExpr} AS soldBaseQuantity,
       {returnedQuantityExpr} AS returnedBaseQuantity,
       {netQuantityExpr} AS netBaseQuantity,
       {netAmountExpr} AS netAmount,
       {discountExpr} AS discountAmount,
       COUNT(DISTINCT movement.SalesInvoiceId) AS invoiceCount
{source}
GROUP BY movement.SaleDate, movement.ProductId
HAVING {netQuantityExpr} <> 0 OR {netAmountExpr} <> 0
ORDER BY periodStart DESC, productName
""",
            "weekly" => $"""
SELECT DATEADD(day, (DATEDIFF(day, @startDate, movement.SaleDate) / 7) * 7, @startDate) AS periodStart,
       CASE WHEN DATEADD(day, (DATEDIFF(day, @startDate, movement.SaleDate) / 7) * 7 + 6, @startDate) > @endDate
            THEN @endDate
            ELSE DATEADD(day, (DATEDIFF(day, @startDate, movement.SaleDate) / 7) * 7 + 6, @startDate)
       END AS periodEnd,
       movement.ProductId AS productId,
       MAX(movement.ProductCode) AS productCode,
       MAX(movement.ProductName) AS productName,
       {soldQuantityExpr} AS soldBaseQuantity,
       {returnedQuantityExpr} AS returnedBaseQuantity,
       {netQuantityExpr} AS netBaseQuantity,
       {netAmountExpr} AS netAmount,
       {discountExpr} AS discountAmount,
       COUNT(DISTINCT movement.SalesInvoiceId) AS invoiceCount
{source}
GROUP BY DATEDIFF(day, @startDate, movement.SaleDate) / 7, movement.ProductId
HAVING {netQuantityExpr} <> 0 OR {netAmountExpr} <> 0
ORDER BY periodStart DESC, productName
""",
            "monthly" => $"""
SELECT DATEFROMPARTS(YEAR(movement.SaleDate), MONTH(movement.SaleDate), 1) AS periodStart,
       CASE WHEN EOMONTH(DATEFROMPARTS(YEAR(movement.SaleDate), MONTH(movement.SaleDate), 1)) > @endDate
            THEN @endDate
            ELSE EOMONTH(DATEFROMPARTS(YEAR(movement.SaleDate), MONTH(movement.SaleDate), 1))
       END AS periodEnd,
       movement.ProductId AS productId,
       MAX(movement.ProductCode) AS productCode,
       MAX(movement.ProductName) AS productName,
       {soldQuantityExpr} AS soldBaseQuantity,
       {returnedQuantityExpr} AS returnedBaseQuantity,
       {netQuantityExpr} AS netBaseQuantity,
       {netAmountExpr} AS netAmount,
       {discountExpr} AS discountAmount,
       COUNT(DISTINCT movement.SalesInvoiceId) AS invoiceCount
{source}
GROUP BY YEAR(movement.SaleDate), MONTH(movement.SaleDate), movement.ProductId
HAVING {netQuantityExpr} <> 0 OR {netAmountExpr} <> 0
ORDER BY periodStart DESC, productName
""",
            "yearly" => $"""
SELECT DATEFROMPARTS(YEAR(movement.SaleDate), 1, 1) AS periodStart,
       CASE WHEN DATEFROMPARTS(YEAR(movement.SaleDate), 12, 31) > @endDate
            THEN @endDate
            ELSE DATEFROMPARTS(YEAR(movement.SaleDate), 12, 31)
       END AS periodEnd,
       movement.ProductId AS productId,
       MAX(movement.ProductCode) AS productCode,
       MAX(movement.ProductName) AS productName,
       {soldQuantityExpr} AS soldBaseQuantity,
       {returnedQuantityExpr} AS returnedBaseQuantity,
       {netQuantityExpr} AS netBaseQuantity,
       {netAmountExpr} AS netAmount,
       {discountExpr} AS discountAmount,
       COUNT(DISTINCT movement.SalesInvoiceId) AS invoiceCount
{source}
GROUP BY YEAR(movement.SaleDate), movement.ProductId
HAVING {netQuantityExpr} <> 0 OR {netAmountExpr} <> 0
ORDER BY periodStart DESC, productName
""",
            _ => throw new InvalidOperationException("Unsupported movement granularity."),
        };
    }
}
