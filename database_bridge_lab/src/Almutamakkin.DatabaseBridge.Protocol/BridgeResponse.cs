using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public sealed record BridgeResponse
{
    [JsonPropertyName("protocolVersion")]
    public required string ProtocolVersion { get; init; }

    [JsonPropertyName("messageType")]
    public required string MessageType { get; init; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("tunnelId")]
    public required string TunnelId { get; init; }

    [JsonPropertyName("respondedAtUtc")]
    public required DateTime RespondedAtUtc { get; init; }

    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("payload")]
    public object? Payload { get; init; }

    [JsonPropertyName("error")]
    public BridgeError? Error { get; init; }
}
