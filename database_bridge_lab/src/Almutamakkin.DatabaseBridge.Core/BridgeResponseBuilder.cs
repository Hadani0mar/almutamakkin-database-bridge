using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

internal static class BridgeResponseBuilder
{
    public static BridgeResponse Success(
        BridgeCommand command,
        object? payload = null) =>
        new()
        {
            ProtocolVersion = BridgeLimits.SupportedProtocolVersion,
            MessageType = MessageTypes.ToResponseType(command.MessageType),
            RequestId = command.RequestId,
            TunnelId = command.TunnelId,
            RespondedAtUtc = DateTime.UtcNow,
            Success = true,
            Payload = payload,
        };

    public static BridgeResponse Failure(
        BridgeCommand command,
        string errorCode,
        string errorMessage,
        string? details = null,
        bool retryable = false) =>
        new()
        {
            ProtocolVersion = BridgeLimits.SupportedProtocolVersion,
            MessageType = MessageTypes.ToResponseType(command.MessageType),
            RequestId = command.RequestId,
            TunnelId = command.TunnelId,
            RespondedAtUtc = DateTime.UtcNow,
            Success = false,
            Error = new BridgeError
            {
                Code = errorCode,
                Message = errorMessage,
                Details = details,
                Retryable = retryable,
            },
        };

    public static BridgeResponse FromValidation(
        BridgeCommand command,
        RequestValidationResult validation) =>
        Failure(
            command,
            validation.ErrorCode ?? ErrorCodes.InvalidMessage,
            validation.ErrorMessage ?? "الطلب غير صالح.",
            validation.Details,
            validation.Retryable);
}
