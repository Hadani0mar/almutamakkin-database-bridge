using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public sealed record SqlExecutionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("resultSets")]
    public List<SqlResultSet> ResultSets { get; init; } = new();

    [JsonPropertyName("affectedRows")]
    public int AffectedRows { get; init; }

    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; init; }

    [JsonPropertyName("wasTruncated")]
    public bool WasTruncated { get; init; }

    [JsonPropertyName("totalReturnedRows")]
    public int TotalReturnedRows { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }
}
