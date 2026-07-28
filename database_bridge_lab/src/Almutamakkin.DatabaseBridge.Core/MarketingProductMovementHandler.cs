using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// Executes the fixed Marketing product-movement contract.
/// The phone supplies only an item id, a bounded period, and a granularity;
/// SQL never crosses the mobile protocol for this feature.
/// </summary>
public sealed class MarketingProductMovementHandler : ICommandHandler
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly ILiveDatabaseProfileResolver _profileResolver;
    private readonly ISqlCommandExecutor _executor;
    private readonly IRequestValidator _validator;
    private readonly IBridgeLogger _logger;

    public MarketingProductMovementHandler(
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

    public string MessageType => MessageTypes.MarketingProductMovement;

    public async Task<BridgeResponse> HandleAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        var payload = BridgeJson.DeserializeMarketingProductMovementPayload(command.Payload);
        var validation = payload is null
            ? RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "تعذر قراءة طلب حركة الصنف.")
            : _validator.ValidateMarketingProductMovementPayload(payload);
        if (!validation.IsValid)
        {
            return BridgeResponseBuilder.FromValidation(command, validation);
        }

        _profileStore.Reload();
        var profile = _profileResolver.Resolve("Marketing");
        if (profile is null || !string.Equals(_profileResolver.GetSystem(profile), "marketing", StringComparison.OrdinalIgnoreCase))
        {
            return BridgeResponseBuilder.Failure(command, ErrorCodes.DatabaseProfileNotFound, "ملف قاعدة Marketing غير متاح.");
        }

        var sqlRequest = new SqlExecutePayload
        {
            DatabaseProfile = "Marketing",
            Sql = BuildQuery(payload!),
            TimeoutSeconds = 30,
            MaxRows = 1200,
            Parameters = new Dictionary<string, SqlParameterValue>
            {
                ["productId"] = new() { Type = "int", Value = payload!.ProductId },
                ["startDate"] = new() { Type = "datetime", Value = payload.StartDate.Date },
                ["endExclusive"] = new() { Type = "datetime", Value = payload.EndDate.Date.AddDays(1) },
            },
        };

        _logger.Info($"Executing named Marketing product movement for item {payload.ProductId} ({payload.Granularity}).");
        var result = await _executor.ExecuteAsync(profile, sqlRequest, cancellationToken);
        if (!result.Success)
        {
            return BridgeResponseBuilder.Failure(command, result.ErrorCode ?? ErrorCodes.SqlExecutionFailed,
                result.ErrorMessage ?? "فشل جلب حركة الصنف.", retryable: result.ErrorCode == ErrorCodes.SqlTimeout);
        }

        return BridgeResponseBuilder.Success(command, new
        {
            system = "marketing",
            operation = "product_movement",
            productId = payload.ProductId,
            executionTimeMs = result.ExecutionTimeMs,
            wasTruncated = result.WasTruncated,
            rows = result.ResultSets.FirstOrDefault()?.Rows ?? [],
        });
    }

    private static string BuildQuery(MarketingProductMovementPayload payload)
    {
        var period = payload.Granularity.ToLowerInvariant() switch
        {
            "daily" => "CONVERT(VARCHAR(10), S_DATE, 120)",
            "monthly" => "CONVERT(VARCHAR(7), S_DATE, 120) + '-01'",
            "yearly" => "CONVERT(VARCHAR(4), S_DATE, 120) + '-01-01'",
            _ => throw new InvalidOperationException("Unsupported movement granularity."),
        };
        var returnPeriod = period.Replace("S_DATE", "S_R_DATE", StringComparison.Ordinal);

        return $"""
SELECT periodStart, periodEnd,
       SUM(soldBaseQuantity) AS soldBaseQuantity,
       SUM(netAmount) AS netAmount,
       SUM(discountAmount) AS discountAmount,
       SUM(invoiceCount) AS invoiceCount
FROM (
  SELECT {period} AS periodStart, {period} AS periodEnd,
         SUM(QTY) AS soldBaseQuantity,
         SUM(
           (((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY))
           - ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100))
           + ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY))
             - ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100))
           ) * ISNULL(S_TAX1, 0) / 100)
           + (((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY))
             - ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100))
           ) + ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY))
             - ((((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100))
           ) * ISNULL(S_TAX1, 0) / 100)) * ISNULL(S_TAX2, 0) / 100)
         ) AS netAmount,
         SUM(((PRICE * QTY) + (ISNULL(COLOR_PRICE, 0) * QTY)) * ISNULL(S_DISCOUNT, 0) / 100) AS discountAmount,
         COUNT(DISTINCT S_ID) AS invoiceCount
  FROM SALE_ITEMS_INVOICE_VIEW
  WHERE ITEM_ID = @productId AND S_STATUES NOT IN (0, 2)
    AND S_DATE >= @startDate AND S_DATE < @endExclusive
  GROUP BY {period}
  UNION ALL
  SELECT {returnPeriod}, {returnPeriod}, -SUM(QTY), -SUM(PRICE * QTY), 0, 0
  FROM R_S_ITEMS_INVOICE_VIEW
  WHERE ITEM_ID = @productId AND S_R_STATUES NOT IN (0, 2)
    AND S_R_DATE >= @startDate AND S_R_DATE < @endExclusive
  GROUP BY {returnPeriod}
) AS combined
GROUP BY periodStart, periodEnd
ORDER BY periodStart ASC
""";
    }
}
