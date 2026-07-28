using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// Executes only a signed catalog package. The requester controls typed values
/// only; the package controls system, SQL, timeout, and response cap.
/// </summary>
public sealed class QueryPackageExecuteHandler : ICommandHandler
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly ILiveDatabaseProfileResolver _profileResolver;
    private readonly IQueryPackageCatalogClient _catalogClient;
    private readonly IQueryPackageSignatureVerifier _signatureVerifier;
    private readonly ISqlCommandExecutor _executor;
    private readonly IQueryClassifier _classifier;
    private readonly IPermissionPolicy _permissionPolicy;
    private readonly IRequestValidator _validator;
    private readonly IBridgeLogger _logger;
    private readonly IActiveRequestTracker _activeRequestTracker;
    private readonly ILiveQueryActivityFeed _activityFeed;

    public QueryPackageExecuteHandler(
        IDatabaseProfileStore profileStore,
        ILiveDatabaseProfileResolver profileResolver,
        IQueryPackageCatalogClient catalogClient,
        IQueryPackageSignatureVerifier signatureVerifier,
        ISqlCommandExecutor executor,
        IQueryClassifier classifier,
        IPermissionPolicy permissionPolicy,
        IRequestValidator validator,
        IBridgeLogger logger,
        IActiveRequestTracker activeRequestTracker,
        ILiveQueryActivityFeed activityFeed)
    {
        _profileStore = profileStore;
        _profileResolver = profileResolver;
        _catalogClient = catalogClient;
        _signatureVerifier = signatureVerifier;
        _executor = executor;
        _classifier = classifier;
        _permissionPolicy = permissionPolicy;
        _validator = validator;
        _logger = logger;
        _activeRequestTracker = activeRequestTracker;
        _activityFeed = activityFeed;
    }

    public string MessageType => MessageTypes.QueryPackageExecute;

    public async Task<BridgeResponse> HandleAsync(BridgeCommand command, CancellationToken cancellationToken)
    {
        var payload = BridgeJson.DeserializeQueryPackageExecutePayload(command.Payload);
        var validation = payload is null
            ? RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "تعذر قراءة طلب حزمة الاستعلام.")
            : _validator.ValidateQueryPackageExecutePayload(payload);
        if (!validation.IsValid)
        {
            return BridgeResponseBuilder.FromValidation(command, validation);
        }

        var package = await _catalogClient.GetAsync(payload!.QueryId, cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return BridgeResponseBuilder.Failure(command, ErrorCodes.InvalidMessage, "حزمة الاستعلام غير موجودة أو غير مفعّلة.");
        }

        if (!string.Equals(package.Definition.QueryId, payload.QueryId, StringComparison.Ordinal))
        {
            return BridgeResponseBuilder.Failure(command, ErrorCodes.InvalidMessage, "حزمة الاستعلام المستلمة لا تطابق الطلب.");
        }

        if (!_signatureVerifier.Verify(package, out var signatureError))
        {
            _logger.Error($"Rejected query package '{payload.QueryId}': {signatureError}");
            return BridgeResponseBuilder.Failure(command, ErrorCodes.SqlPermissionDenied, "تعذر التحقق من سلامة حزمة الاستعلام.");
        }

        if (!ValidateParameters(package.Definition, payload.Parameters, out var parameterError))
        {
            return BridgeResponseBuilder.Failure(command, ErrorCodes.InvalidMessage, parameterError!);
        }

        _profileStore.Reload();
        var profile = _profileResolver.Resolve(package.Definition.DatabaseProfile);
        if (profile is null)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseProfileNotFound,
                "لا يوجد اتصال نشط يطابق نظام حزمة الاستعلام.");
        }

        var system = _profileResolver.GetSystem(profile);
        if (!string.Equals(system, package.Definition.System, StringComparison.OrdinalIgnoreCase))
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseProfileNotFound,
                "لا يوجد اتصال نشط يطابق نظام حزمة الاستعلام.");
        }

        var sqlRequest = new SqlExecutePayload
        {
            DatabaseProfile = package.Definition.DatabaseProfile,
            Sql = package.Definition.Sql,
            Parameters = payload.Parameters,
            TimeoutSeconds = package.Definition.TimeoutSeconds,
            MaxRows = package.Definition.MaxRows,
        };
        var sqlValidation = _validator.ValidateSqlExecutePayload(sqlRequest);
        if (!sqlValidation.IsValid)
        {
            return BridgeResponseBuilder.FromValidation(command, sqlValidation);
        }

        var classification = _classifier.Classify(sqlRequest.Sql);
        if (classification != QueryClassification.Read || QueryClassifier.ContainsForbiddenDataChange(sqlRequest.Sql))
        {
            return BridgeResponseBuilder.Failure(command, ErrorCodes.SqlPermissionDenied, "حزمة الاستعلام ليست قراءة فقط.");
        }

        var permission = _permissionPolicy.Evaluate(profile, sqlRequest.Sql, classification);
        if (!permission.IsAllowed)
        {
            return BridgeResponseBuilder.Failure(command, ErrorCodes.SqlPermissionDenied, permission.ErrorMessage ?? "الاستعلام غير مسموح.");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeRequestTracker.Register(command.RequestId, linkedCts);
        _activityFeed.Begin(new LiveQueryActivity(
            command.RequestId,
            DateTime.UtcNow,
            system,
            profile.ConnectionKind,
            $"حزمة: {package.Definition.QueryId}"));

        try
        {
            _logger.Info($"Executing signed package '{package.Definition.QueryId}' v{package.Definition.Version} on '{profile.ProfileName}' → {profile.DatabaseName} [{system}].");
            var result = await _executor.ExecuteAsync(profile, sqlRequest, linkedCts.Token).ConfigureAwait(false);
            if (!result.Success)
            {
                return BridgeResponseBuilder.Failure(
                    command,
                    result.ErrorCode ?? ErrorCodes.SqlExecutionFailed,
                    result.ErrorMessage ?? "فشل تنفيذ حزمة الاستعلام.",
                    retryable: string.Equals(result.ErrorCode, ErrorCodes.SqlTimeout, StringComparison.Ordinal));
            }

            return BridgeResponseBuilder.Success(command, new
            {
                queryId = package.Definition.QueryId,
                packageVersion = package.Definition.Version,
                system,
                resolvedProfile = profile.ProfileName,
                resolvedDatabase = profile.DatabaseName,
                executionTimeMs = result.ExecutionTimeMs,
                affectedRows = result.AffectedRows,
                totalReturnedRows = result.TotalReturnedRows,
                wasTruncated = result.WasTruncated,
                resultSets = result.ResultSets,
            });
        }
        finally
        {
            _activityFeed.End(command.RequestId);
            _activeRequestTracker.Complete(command.RequestId);
        }
    }

    private static bool ValidateParameters(
        QueryPackageDefinition definition,
        IReadOnlyDictionary<string, SqlParameterValue> supplied,
        out string? error)
    {
        var declared = definition.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        if (declared.Count != definition.Parameters.Count)
        {
            error = "حزمة الاستعلام تحتوي معاملات مكررة.";
            return false;
        }

        foreach (var name in supplied.Keys)
        {
            if (!declared.ContainsKey(name))
            {
                error = "الطلب يحتوي معاملاً غير معرّف في الحزمة.";
                return false;
            }
        }

        foreach (var parameter in definition.Parameters)
        {
            if (!supplied.TryGetValue(parameter.Name, out var value))
            {
                if (parameter.Required)
                {
                    error = "معامل مطلوب غير موجود في الطلب.";
                    return false;
                }
                continue;
            }

            if (!string.Equals(parameter.Type, value.Type, StringComparison.OrdinalIgnoreCase))
            {
                error = "نوع أحد معاملات الطلب لا يطابق حزمة الاستعلام.";
                return false;
            }
        }

        error = null;
        return true;
    }
}
