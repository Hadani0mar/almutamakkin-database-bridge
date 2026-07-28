using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public sealed record SqlExecutePayload
{
    [JsonPropertyName("databaseProfile")]
    public required string DatabaseProfile { get; init; }

    /// <summary>
    /// Optional Initial Catalog override. Phone keeps databaseProfile = Marketing
    /// and selects a same-schema backup database name here.
    /// </summary>
    [JsonPropertyName("catalog")]
    public string? Catalog { get; init; }

    [JsonPropertyName("sql")]
    public required string Sql { get; init; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, SqlParameterValue> Parameters { get; init; } = new();

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; init; } = 30;

    [JsonPropertyName("maxRows")]
    public int MaxRows { get; init; } = 1000;
}
