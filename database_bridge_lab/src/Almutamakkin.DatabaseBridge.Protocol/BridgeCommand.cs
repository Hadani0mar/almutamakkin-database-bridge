using System.Text.Json;
using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public sealed record BridgeCommand
{
    [JsonPropertyName("protocolVersion")]
    public required string ProtocolVersion { get; init; }

    [JsonPropertyName("messageType")]
    public required string MessageType { get; init; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("tunnelId")]
    public required string TunnelId { get; init; }

    [JsonPropertyName("sentAtUtc")]
    public required DateTime SentAtUtc { get; init; }

    [JsonPropertyName("payload")]
    public required JsonElement Payload { get; init; }
}
