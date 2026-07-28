using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public sealed record SqlResultSet
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("columns")]
    public List<SqlColumnDefinition> Columns { get; init; } = new();

    [JsonPropertyName("rows")]
    public List<Dictionary<string, object?>> Rows { get; init; } = new();

    [JsonPropertyName("wasTruncated")]
    public bool WasTruncated { get; init; }
}
