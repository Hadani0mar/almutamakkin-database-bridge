using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class SqlExecuteHandler : ICommandHandler
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly ILiveDatabaseProfileResolver _profileResolver;
    private readonly ISqlCommandExecutor _executor;
    private readonly IQueryClassifier _classifier;
    private readonly IPermissionPolicy _permissionPolicy;
    private readonly IRequestValidator _validator;
    private readonly IBridgeLogger _logger;
    private readonly IActiveRequestTracker _activeRequestTracker;
    private readonly ILiveQueryActivityFeed _activityFeed;

    public SqlExecuteHandler(
        IDatabaseProfileStore profileStore,
        ILiveDatabaseProfileResolver profileResolver,
        ISqlCommandExecutor executor,
        IQueryClassifier classifier,
        IPermissionPolicy permissionPolicy,
        IRequestValidator validator,
        IBridgeLogger logger,
        IActiveRequestTracker activeRequestTracker,
        ILiveQueryActivityFeed activityFeed)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _permissionPolicy = permissionPolicy ?? throw new ArgumentNullException(nameof(permissionPolicy));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activeRequestTracker = activeRequestTracker ?? throw new ArgumentNullException(nameof(activeRequestTracker));
        _activityFeed = activityFeed ?? throw new ArgumentNullException(nameof(activityFeed));
    }

    public string MessageType => MessageTypes.SqlExecute;

    public async Task<BridgeResponse> HandleAsync(
        BridgeCommand command,
        CancellationToken cancellationToken)
    {
        var payload = BridgeJson.DeserializeSqlExecutePayload(command.Payload);
        if (payload is null)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.InvalidMessage,
                "تعذر قراءة حمولة sql.execute.");
        }

        var payloadValidation = _validator.ValidateSqlExecutePayload(payload);
        if (!payloadValidation.IsValid)
        {
            return BridgeResponseBuilder.FromValidation(command, payloadValidation);
        }

        _profileStore.Reload();
        var profile = _profileResolver.Resolve(payload.DatabaseProfile);
        if (profile is null)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseProfileNotFound,
                $"لا يوجد ملف اتصال مفعّل يطابق القاعدة المطلوبة. تحقق من databaseProfile ثم أعد المحاولة.");
        }

        var executionProfile = profile;
        var resolvedCatalog = profile.DatabaseName;
        if (!string.IsNullOrWhiteSpace(payload.Catalog))
        {
            if (!SqlCatalogName.TryNormalize(payload.Catalog, out var catalog, out var catalogError))
            {
                return BridgeResponseBuilder.Failure(
                    command,
                    ErrorCodes.InvalidMessage,
                    catalogError ?? "اسم القاعدة غير صالح.");
            }

            if (!string.Equals(catalog, profile.DatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                // Do not call sys.databases on every sql.execute — that doubled
                // latency for legacy-inventory mode. Invalid catalogs fail on open.
                executionProfile = CloneWithCatalog(profile, catalog);
                resolvedCatalog = catalog;
            }
            else
            {
                resolvedCatalog = catalog;
            }
        }

        var classification = _classifier.Classify(payload.Sql);
        if (classification == QueryClassification.Unknown)
        {
            // Harmless unknown batches (e.g. obscure session statements) may
            // still run under ReadOnly if they do not mutate data.
            if (QueryClassifier.ContainsForbiddenDataChange(payload.Sql))
            {
                return BridgeResponseBuilder.Failure(
                    command,
                    ErrorCodes.SqlPermissionDenied,
                    "غير مسموح: إضافة أو تعديل أو حذف البيانات (INSERT/UPDATE/DELETE).");
            }

            classification = QueryClassification.Read;
        }

        var permission = _permissionPolicy.Evaluate(executionProfile, payload.Sql, classification);
        if (!permission.IsAllowed)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.SqlPermissionDenied,
                permission.ErrorMessage ?? "الاستعلام غير مسموح.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeRequestTracker.Register(command.RequestId, linkedCts);

        var system = _profileResolver.GetSystem(profile);
        _activityFeed.Begin(new LiveQueryActivity(
            command.RequestId,
            DateTime.UtcNow,
            system,
            profile.ConnectionKind,
            LiveQueryNameResolver.Resolve(payload.Sql, system)));

        try
        {
            _logger.Info(
                $"Executing SQL for request {command.RequestId} on profile '{profile.ProfileName}' → {resolvedCatalog} [{system}] ({classification}).");

            var executionResult = await _executor.ExecuteAsync(executionProfile, payload, linkedCts.Token);
            if (!executionResult.Success)
            {
                _logger.Error($"SQL execution failed for request {command.RequestId}: {executionResult.ErrorMessage}");
                return BridgeResponseBuilder.Failure(
                    command,
                    executionResult.ErrorCode ?? ErrorCodes.SqlExecutionFailed,
                    executionResult.ErrorMessage ?? "فشل تنفيذ SQL.",
                    retryable: string.Equals(
                        executionResult.ErrorCode,
                        ErrorCodes.SqlTimeout,
                        StringComparison.Ordinal));
            }

            _logger.Info($"SQL execution successful for request {command.RequestId}. Affected: {executionResult.AffectedRows}, Returned: {executionResult.TotalReturnedRows}");

            var responsePayload = new
            {
                databaseProfile = payload.DatabaseProfile,
                resolvedProfile = profile.ProfileName,
                resolvedDatabase = resolvedCatalog,
                catalog = string.IsNullOrWhiteSpace(payload.Catalog) ? null : resolvedCatalog,
                system,
                classification = classification.ToString(),
                executionTimeMs = executionResult.ExecutionTimeMs,
                affectedRows = executionResult.AffectedRows,
                totalReturnedRows = executionResult.TotalReturnedRows,
                wasTruncated = executionResult.WasTruncated,
                resultSets = executionResult.ResultSets,
            };

            return BridgeResponseBuilder.Success(command, responsePayload);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.SqlExecutionFailed,
                "تم إلغاء تنفيذ الاستعلام.");
        }
        catch (Exception ex)
        {
            _logger.Error($"SQL execution failed for request {command.RequestId}.", ex);
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.InternalError,
                "حدث خطأ داخلي أثناء تنفيذ SQL.");
        }
        finally
        {
            _activityFeed.End(command.RequestId);
            _activeRequestTracker.Complete(command.RequestId);
        }
    }

    private static DatabaseProfile CloneWithCatalog(DatabaseProfile profile, string catalog) =>
        new()
        {
            Id = profile.Id,
            ProfileName = profile.ProfileName,
            ServerName = profile.ServerName,
            DatabaseName = catalog,
            ConnectionKind = profile.ConnectionKind,
            AuthenticationMode = profile.AuthenticationMode,
            UserName = profile.UserName,
            EncryptedPassword = profile.EncryptedPassword,
            TrustServerCertificate = profile.TrustServerCertificate,
            EncryptConnection = profile.EncryptConnection,
            IsEnabled = profile.IsEnabled,
            PermissionLevel = profile.PermissionLevel,
            CustomPermissions = profile.CustomPermissions,
            CommandTimeoutSeconds = profile.CommandTimeoutSeconds,
            MaximumRows = profile.MaximumRows,
        };
}

/// <summary>
/// Maps live SQL text to a short Arabic label for the bridge dashboard.
/// </summary>
public static class LiveQueryNameResolver
{
    public static string Resolve(string sql, string system)
    {
        var value = sql ?? string.Empty;
        var upper = value.ToUpperInvariant();
        var isInfinity = string.Equals(system, "infinity", StringComparison.OrdinalIgnoreCase);

        if (ContainsAny(upper, "CATEOGRY3", "EXPIRYDATE", "NEAREST_EXPIRY", "EXPIRED"))
        {
            return "الصلاحية / المنتهيات";
        }

        if (ContainsAny(upper, "SHORTAGESTATUS", "SUGGESTEDORDERQTY", "DAYSOFSTOCKCOVER", "TARGETSTOCK35"))
        {
            return "نواقص المخزون";
        }

        if (ContainsAny(upper, "DEBTAMOUNT", "TOTAL_DEBT", "TOTALDEBT") ||
            (ContainsAny(upper, "CUSTOMERS", "CUST_") && ContainsAny(upper, "BUY_ITEMS", "TAKE_VIEW", "GIVE")))
        {
            return ContainsAny(upper, "SUPPLIER", "BUYS", "BUY_ITEMS")
                ? "ديون الموردين"
                : "ديون العملاء";
        }

        if (ContainsAny(upper, "BARCODE", "ITEM_NAME", "PRODUCTNAME", "PRODUCTCODE") &&
            ContainsAny(upper, "LIKE", "TOP ", "WHERE"))
        {
            return isInfinity ? "بحث أصناف إنفينيتي" : "بحث الأصناف";
        }

        if (ContainsAny(upper, "USER_TIME_SHEET", "DAILY", "STATISTICS") ||
            (ContainsAny(upper, "SALE") && ContainsAny(upper, "EMPLOYEE", "USER_ID")))
        {
            return "إحصائيات اليوم";
        }

        if (ContainsAny(upper, "S_STATUES", "ACTIVE_INVOICE", "CUST_ID = 0", "MUTAMAKKIN_HIK_SHIFTS"))
        {
            return "الفاتورة المفتوحة";
        }

        if (ContainsAny(upper, "QYT", "SUBTOTAL", "SALES_MOVEMENT", "PURCHASE_MOVEMENT") ||
            (ContainsAny(upper, "SALE_ITEMS", "BUY_ITEMS") && ContainsAny(upper, "GROUP BY", "DATE")))
        {
            return ContainsAny(upper, "BUY", "PURCHASE")
                ? "حركة المشتريات"
                : "حركة المبيعات";
        }

        if (ContainsAny(upper, "SITTEINGS", "BUSINESS_PROFILE", "CONFIG_BRANCHS", "MYCOMPANY"))
        {
            return isInfinity ? "بيانات الفرع" : "بيانات النشاط";
        }

        if (ContainsAny(upper, "STOCKONHAND", "DATA_PRODUCTS", "DATA_PRODUCTINVENTORIES"))
        {
            return "بطاقة صنف إنفينيتي";
        }

        if (ContainsAny(upper, "REQUIRED", "NETREQUIRED", "COVER"))
        {
            return isInfinity ? "الأصناف المطلوبة" : "المطلوب شراؤه";
        }

        if (ContainsAny(upper, "SMART", "PURCHASE_ORDER", "FORECAST"))
        {
            return "طلبية الشراء الذكية";
        }

        if (ContainsAny(upper, "SHIFT"))
        {
            return "تقرير الورديات";
        }

        return isInfinity ? "استعلام إنفينيتي" : "استعلام أبوغريس";
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
