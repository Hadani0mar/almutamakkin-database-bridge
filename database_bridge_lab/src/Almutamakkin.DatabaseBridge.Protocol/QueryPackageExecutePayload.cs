using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

/// <summary>
/// Mobile-safe query request. SQL, catalog names, row limits, and timeouts are
/// deliberately absent: they are owned by the signed server-side package.
/// </summary>
public sealed record QueryPackageExecutePayload
{
    [JsonPropertyName("queryId")]
    public required string QueryId { get; init; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, SqlParameterValue> Parameters { get; init; } = new();
}
