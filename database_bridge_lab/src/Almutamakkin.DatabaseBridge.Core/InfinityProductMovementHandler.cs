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
                ["startDate"] = new() { Type = "datetime", Value = payload.StartDate.Date },
                ["endDate"] = new() { Type = "datetime", Value = payload.EndDate.Date },
                ["endExclusive"] = new() { Type = "datetime", Value = payload.EndDate.Date.AddDays(1) },
            },
        };

        _logger.Info($"Executing named Infinity product movement for item {payload.ProductId} ({payload.Granularity}).");
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
            executionTimeMs = result.ExecutionTimeMs,
            wasTruncated = result.WasTruncated,
            rows = result.ResultSets.FirstOrDefault()?.Rows ?? [],
        });
    }

    private static string BuildQuery(InfinityProductMovementPayload payload)
    {
        const string quantityExpr = "SUM(ii.QYT * ISNULL(ii.UnitBaseQYT, 1))";
        const string netAmountExpr = "SUM(ii.SubTotal - ISNULL(ii.DiscountAmount, 0))";
        const string discountExpr = "SUM(ISNULL(ii.DiscountAmount, 0))";
        const string source = """
FROM SALES.Data_SalesInvoiceItems AS ii
INNER JOIN SALES.Data_SalesInvoices AS i
    ON i.SalesInvoiceID_PK = ii.SalesInvoiceID_FK
WHERE ii.ProductID_FK = @productId
  AND i.DocumentTypeID_FK IN (15, 16)
  AND i.SalesInvoiceStateID_FK IN (200, 300)
  AND i.SalesInvoiceDate >= @startDate
  AND i.SalesInvoiceDate < @endExclusive
""";

        return payload.Granularity.ToLowerInvariant() switch
        {
            "daily" => $"""
SELECT CAST(i.SalesInvoiceDate AS date) AS periodStart,
       CAST(i.SalesInvoiceDate AS date) AS periodEnd,
       {quantityExpr} AS soldBaseQuantity,
       {netAmountExpr} AS netAmount,
       {discountExpr} AS discountAmount,
       COUNT(DISTINCT i.SalesInvoiceID_PK) AS invoiceCount
{source}
GROUP BY CAST(i.SalesInvoiceDate AS date)
HAVING {quantityExpr} <> 0 OR {netAmountExpr} <> 0
ORDER BY periodStart DESC
""",
            "weekly" => $"""
SELECT DATEADD(day, (DATEDIFF(day, @startDate, CAST(i.SalesInvoiceDate AS date)) / 7) * 7, @startDate) AS periodStart,
       CASE WHEN DATEADD(day, (DATEDIFF(day, @startDate, CAST(i.SalesInvoiceDate AS date)) / 7) * 7 + 6, @startDate) > @endDate
            THEN @endDate
            ELSE DATEADD(day, (DATEDIFF(day, @startDate, CAST(i.SalesInvoiceDate AS date)) / 7) * 7 + 6, @startDate)
       END AS periodEnd,
       {quantityExpr} AS soldBaseQuantity,
       {netAmountExpr} AS netAmount,
       {discountExpr} AS discountAmount,
       COUNT(DISTINCT i.SalesInvoiceID_PK) AS invoiceCount
{source}
GROUP BY DATEDIFF(day, @startDate, CAST(i.SalesInvoiceDate AS date)) / 7
HAVING {quantityExpr} <> 0 OR {netAmountExpr} <> 0
ORDER BY periodStart DESC
""",
            "monthly" => $"""
SELECT DATEFROMPARTS(YEAR(i.SalesInvoiceDate), MONTH(i.SalesInvoiceDate), 1) AS periodStart,
       CASE WHEN EOMONTH(DATEFROMPARTS(YEAR(i.SalesInvoiceDate), MONTH(i.SalesInvoiceDate), 1)) > @endDate
            THEN @endDate
            ELSE EOMONTH(DATEFROMPARTS(YEAR(i.SalesInvoiceDate), MONTH(i.SalesInvoiceDate), 1))
       END AS periodEnd,
       {quantityExpr} AS soldBaseQuantity,
       {netAmountExpr} AS netAmount,
       {discountExpr} AS discountAmount,
       COUNT(DISTINCT i.SalesInvoiceID_PK) AS invoiceCount
{source}
GROUP BY YEAR(i.SalesInvoiceDate), MONTH(i.SalesInvoiceDate)
HAVING {quantityExpr} <> 0 OR {netAmountExpr} <> 0
ORDER BY periodStart DESC
""",
            "yearly" => $"""
SELECT DATEFROMPARTS(YEAR(i.SalesInvoiceDate), 1, 1) AS periodStart,
       CASE WHEN DATEFROMPARTS(YEAR(i.SalesInvoiceDate), 12, 31) > @endDate
            THEN @endDate
            ELSE DATEFROMPARTS(YEAR(i.SalesInvoiceDate), 12, 31)
       END AS periodEnd,
       {quantityExpr} AS soldBaseQuantity,
       {netAmountExpr} AS netAmount,
       {discountExpr} AS discountAmount,
       COUNT(DISTINCT i.SalesInvoiceID_PK) AS invoiceCount
{source}
GROUP BY YEAR(i.SalesInvoiceDate)
HAVING {quantityExpr} <> 0 OR {netAmountExpr} <> 0
ORDER BY periodStart DESC
""",
            _ => throw new InvalidOperationException("Unsupported movement granularity."),
        };
    }
}
