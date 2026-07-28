using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public sealed record BridgeError
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("details")]
    public string? Details { get; init; }

    [JsonPropertyName("retryable")]
    public bool Retryable { get; init; }
}
