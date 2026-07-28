using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public interface IRequestValidator
{
    RequestValidationResult ValidateCommand(BridgeCommand command);

    RequestValidationResult ValidateSqlExecutePayload(SqlExecutePayload payload);

    RequestValidationResult ValidateQueryPackageExecutePayload(QueryPackageExecutePayload payload);

    RequestValidationResult ValidateDatabaseTestPayload(DatabaseTestPayload payload);

    RequestValidationResult ValidateMarketingProductMovementPayload(MarketingProductMovementPayload payload);

    RequestValidationResult ValidateInfinityProductMovementPayload(InfinityProductMovementPayload payload);
}

public sealed record RequestValidationResult
{
    public bool IsValid { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string? Details { get; init; }

    public bool Retryable { get; init; }

    public static RequestValidationResult Success() =>
        new() { IsValid = true };

    public static RequestValidationResult Failure(
        string errorCode,
        string errorMessage,
        string? details = null,
        bool retryable = false) =>
        new()
        {
            IsValid = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Details = details,
            Retryable = retryable,
        };
}
