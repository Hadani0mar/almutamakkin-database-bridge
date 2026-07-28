using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public sealed record SqlColumnDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("dataType")]
    public required string DataType { get; init; }

    [JsonPropertyName("allowNull")]
    public bool AllowNull { get; init; }
}
