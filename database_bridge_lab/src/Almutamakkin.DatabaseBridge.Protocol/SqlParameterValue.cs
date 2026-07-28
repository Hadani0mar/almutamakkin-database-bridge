using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public sealed record SqlParameterValue
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("value")]
    public object? Value { get; init; }
}
