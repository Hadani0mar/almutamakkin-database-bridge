using System.Text.Json;
using System.Text.RegularExpressions;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed partial class RequestValidator : IRequestValidator
{
    private static readonly HashSet<string> SupportedParameterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string",
        "int",
        "long",
        "decimal",
        "double",
        "bool",
        "datetime",
        "guid",
        "null",
    };

    private readonly AppSettings _settings;

    public RequestValidator(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public RequestValidationResult ValidateCommand(BridgeCommand command)
    {
        if (command is null)
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                "الرسالة غير صالحة.");
        }

        if (!string.Equals(command.ProtocolVersion, BridgeLimits.SupportedProtocolVersion, StringComparison.Ordinal))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.UnsupportedProtocol,
                $"إصدار البروتوكول '{command.ProtocolVersion}' غير مدعوم.");
        }

        if (string.IsNullOrWhiteSpace(command.MessageType) ||
            !MessageTypes.Commands.Contains(command.MessageType))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.UnsupportedCommand,
                $"نوع الرسالة '{command.MessageType}' غير مدعوم.");
        }

        if (string.IsNullOrWhiteSpace(command.RequestId))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidRequestId,
                "معرف الطلب مطلوب.");
        }

        if (!string.Equals(command.TunnelId, _settings.TunnelId, StringComparison.Ordinal))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidTunnelId,
                "معرف النفق لا يطابق الجسر المحلي.");
        }

        var maxAge = TimeSpan.FromMinutes(_settings.MaximumRequestAgeMinutes);
        if (DateTime.UtcNow - command.SentAtUtc.ToUniversalTime() > maxAge)
        {
            return RequestValidationResult.Failure(
                ErrorCodes.RequestExpired,
                "انتهت صلاحية الطلب.");
        }

        if (ContainsSensitiveData(command))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                "الطلب يحتوي على بيانات حساسة غير مسموح بها.");
        }

        return command.MessageType switch
        {
            MessageTypes.SqlExecute => ValidateSqlExecuteCommand(command),
            MessageTypes.QueryPackageExecute => ValidateQueryPackageExecuteCommand(command),
            MessageTypes.DatabaseTest => ValidateDatabaseTestCommand(command),
            MessageTypes.DatabaseList => ValidateDatabaseListCommand(command),
            MessageTypes.MarketingProductMovement => ValidateMarketingProductMovementCommand(command),
            MessageTypes.InfinityProductMovement => ValidateInfinityProductMovementCommand(command),
            MessageTypes.ChangesProbe => ValidateChangesProbeCommand(command),
            MessageTypes.ChangesPull => ValidateChangesPullCommand(command),
            _ => RequestValidationResult.Success(),
        };
    }

    public RequestValidationResult ValidateSqlExecutePayload(SqlExecutePayload payload)
    {
        if (payload is null)
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                "حمولة تنفيذ SQL غير صالحة.");
        }

        if (string.IsNullOrWhiteSpace(payload.DatabaseProfile))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.DatabaseProfileNotFound,
                "اسم ملف قاعدة البيانات مطلوب.");
        }

        if (string.IsNullOrWhiteSpace(payload.Sql))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.SqlEmpty,
                "نص SQL فارغ.");
        }

        if (payload.Sql.Length > _settings.MaxSqlLength)
        {
            return RequestValidationResult.Failure(
                ErrorCodes.SqlTooLong,
                $"طول SQL يتجاوز الحد {_settings.MaxSqlLength}.");
        }

        if (payload.TimeoutSeconds <= 0 ||
            payload.TimeoutSeconds > _settings.MaximumTimeoutSeconds)
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                $"مهلة التنفيذ يجب أن تكون بين 1 و {_settings.MaximumTimeoutSeconds} ثانية.");
        }

        if (payload.MaxRows <= 0 ||
            payload.MaxRows > _settings.MaximumMaxRows)
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                $"الحد الأقصى للصفوف يجب أن يكون بين 1 و {_settings.MaximumMaxRows}.");
        }

        if (!string.IsNullOrWhiteSpace(payload.Catalog) &&
            !SqlCatalogName.TryNormalize(payload.Catalog, out _, out var catalogError))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                catalogError ?? "اسم القاعدة غير صالح.");
        }

        foreach (var (name, parameter) in payload.Parameters)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return RequestValidationResult.Failure(
                    ErrorCodes.InvalidMessage,
                    "اسم المعامل غير صالح.");
            }

            var parameterValidation = ValidateParameter(name, parameter);
            if (!parameterValidation.IsValid)
            {
                return parameterValidation;
            }
        }

        return RequestValidationResult.Success();
    }

    public RequestValidationResult ValidateDatabaseTestPayload(DatabaseTestPayload payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.DatabaseProfile))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.DatabaseProfileNotFound,
                "اسم ملف قاعدة البيانات مطلوب.");
        }

        return RequestValidationResult.Success();
    }

    public RequestValidationResult ValidateQueryPackageExecutePayload(QueryPackageExecutePayload payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.QueryId) ||
            !QueryPackageIdRegex().IsMatch(payload.QueryId))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                "معرّف حزمة الاستعلام غير صالح.");
        }

        foreach (var (name, parameter) in payload.Parameters)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return RequestValidationResult.Failure(
                    ErrorCodes.InvalidMessage,
                    "اسم معامل حزمة الاستعلام غير صالح.");
            }

            var parameterValidation = ValidateParameter(name, parameter);
            if (!parameterValidation.IsValid)
            {
                return parameterValidation;
            }
        }

        return RequestValidationResult.Success();
    }

    public RequestValidationResult ValidateMarketingProductMovementPayload(MarketingProductMovementPayload payload)
    {
        if (payload is null || payload.ProductId <= 0)
        {
            return RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "رقم الصنف مطلوب.");
        }

        if (payload.StartDate == default || payload.EndDate == default || payload.EndDate < payload.StartDate)
        {
            return RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "فترة حركة الصنف غير صالحة.");
        }

        if (payload.EndDate.Date - payload.StartDate.Date > TimeSpan.FromDays(3660))
        {
            return RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "فترة حركة الصنف كبيرة جداً.");
        }

        if (!new[] { "daily", "monthly", "yearly" }.Contains(payload.Granularity, StringComparer.OrdinalIgnoreCase))
        {
            return RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "مستوى تجميع حركة الصنف غير مدعوم.");
        }

        return RequestValidationResult.Success();
    }

    public RequestValidationResult ValidateInfinityProductMovementPayload(InfinityProductMovementPayload payload)
    {
        if (payload is null || payload.ProductId <= 0)
        {
            return RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "رقم الصنف مطلوب.");
        }

        if (payload.StartDate == default || payload.EndDate == default || payload.EndDate < payload.StartDate)
        {
            return RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "فترة حركة الصنف غير صالحة.");
        }

        if (payload.EndDate.Date - payload.StartDate.Date > TimeSpan.FromDays(3660))
        {
            return RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "فترة حركة الصنف كبيرة جداً.");
        }

        if (!new[] { "daily", "weekly", "monthly", "yearly" }.Contains(payload.Granularity, StringComparer.OrdinalIgnoreCase))
        {
            return RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "مستوى تجميع حركة الصنف غير مدعوم.");
        }

        return RequestValidationResult.Success();
    }

    private RequestValidationResult ValidateSqlExecuteCommand(BridgeCommand command)
    {
        var payload = BridgeJson.DeserializeSqlExecutePayload(command.Payload);
        if (payload is null)
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                "تعذر قراءة حمولة sql.execute.");
        }

        return ValidateSqlExecutePayload(payload);
    }

    private RequestValidationResult ValidateQueryPackageExecuteCommand(BridgeCommand command)
    {
        var payload = BridgeJson.DeserializeQueryPackageExecutePayload(command.Payload);
        return payload is null
            ? RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                "تعذر قراءة طلب حزمة الاستعلام.")
            : ValidateQueryPackageExecutePayload(payload);
    }

    private RequestValidationResult ValidateDatabaseTestCommand(BridgeCommand command)
    {
        var payload = BridgeJson.DeserializeDatabaseTestPayload(command.Payload);
        if (payload is null)
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                "تعذر قراءة حمولة database.test.");
        }

        return ValidateDatabaseTestPayload(payload);
    }

    private RequestValidationResult ValidateDatabaseListCommand(BridgeCommand command)
    {
        var payload = BridgeJson.DeserializeDatabaseListPayload(command.Payload);
        if (payload is null || string.IsNullOrWhiteSpace(payload.DatabaseProfile))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                "تعذر قراءة حمولة database.list.");
        }

        return RequestValidationResult.Success();
    }

    private RequestValidationResult ValidateMarketingProductMovementCommand(BridgeCommand command)
    {
        var payload = BridgeJson.DeserializeMarketingProductMovementPayload(command.Payload);
        return payload is null
            ? RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "تعذر قراءة طلب حركة الصنف.")
            : ValidateMarketingProductMovementPayload(payload);
    }

    private RequestValidationResult ValidateInfinityProductMovementCommand(BridgeCommand command)
    {
        var payload = BridgeJson.DeserializeInfinityProductMovementPayload(command.Payload);
        return payload is null
            ? RequestValidationResult.Failure(ErrorCodes.InvalidMessage, "تعذر قراءة طلب حركة الصنف.")
            : ValidateInfinityProductMovementPayload(payload);
    }

    private RequestValidationResult ValidateChangesProbeCommand(BridgeCommand command)
    {
        var payload = BridgeJson.DeserializeChangesProbePayload(command.Payload);
        return ValidateChangeDomainKeys(payload?.Domains);
    }

    private RequestValidationResult ValidateChangesPullCommand(BridgeCommand command)
    {
        var payload = BridgeJson.DeserializeChangesPullPayload(command.Payload);
        return ValidateChangeDomainKeys(payload?.Domains);
    }

    /// <summary>
    /// An omitted/empty list means "report every known domain" — only
    /// entries the caller actually supplies must be well-formed.
    /// </summary>
    private static RequestValidationResult ValidateChangeDomainKeys(List<ChangeDomainKey>? domains)
    {
        if (domains is null)
        {
            return RequestValidationResult.Success();
        }

        foreach (var key in domains)
        {
            if (key is null ||
                string.IsNullOrWhiteSpace(key.System) ||
                string.IsNullOrWhiteSpace(key.Domain))
            {
                return RequestValidationResult.Failure(
                    ErrorCodes.InvalidMessage,
                    "كل نطاق مراقبة يحتاج system و domain.");
            }
        }

        return RequestValidationResult.Success();
    }

    private static RequestValidationResult ValidateParameter(string name, SqlParameterValue parameter)
    {
        if (parameter is null || string.IsNullOrWhiteSpace(parameter.Type))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                $"نوع المعامل '{name}' غير معروف.");
        }

        if (!SupportedParameterTypes.Contains(parameter.Type))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                $"نوع المعامل '{parameter.Type}' غير مدعوم.");
        }

        if (parameter.Type.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return RequestValidationResult.Success();
        }

        if (parameter.Value is null)
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                $"قيمة المعامل '{name}' مطلوبة.");
        }

        if (!IsCompatibleValue(parameter.Type, parameter.Value))
        {
            return RequestValidationResult.Failure(
                ErrorCodes.InvalidMessage,
                $"قيمة المعامل '{name}' لا تطابق نوعه '{parameter.Type}'.");
        }

        return RequestValidationResult.Success();
    }

    private static bool IsCompatibleValue(string type, object value)
    {
        return type.ToLowerInvariant() switch
        {
            "string" => value is string or JsonElement { ValueKind: JsonValueKind.String },
            "int" => value is int or long or JsonElement { ValueKind: JsonValueKind.Number },
            "long" => value is long or int or JsonElement { ValueKind: JsonValueKind.Number },
            "decimal" => value is decimal or double or float or int or long or JsonElement { ValueKind: JsonValueKind.Number },
            "double" => value is double or float or decimal or int or long or JsonElement { ValueKind: JsonValueKind.Number },
            "bool" => value is bool or JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False },
            "datetime" => value is DateTime or DateTimeOffset or string or JsonElement { ValueKind: JsonValueKind.String },
            "guid" => value is Guid or string or JsonElement { ValueKind: JsonValueKind.String },
            _ => false,
        };
    }

    private static bool ContainsSensitiveData(BridgeCommand command)
    {
        var raw = command.Payload.GetRawText();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return SensitivePatternRegex().IsMatch(raw);
    }

    [GeneratedRegex(
        @"(?i)(password|pwd|connection\s*string|encryptedpassword|secret|api[_-]?key)\s*[:=]",
        RegexOptions.CultureInvariant)]
    private static partial Regex SensitivePatternRegex();

    [GeneratedRegex("^[a-z][a-z0-9_.-]{2,119}$", RegexOptions.CultureInvariant)]
    private static partial Regex QueryPackageIdRegex();
}
