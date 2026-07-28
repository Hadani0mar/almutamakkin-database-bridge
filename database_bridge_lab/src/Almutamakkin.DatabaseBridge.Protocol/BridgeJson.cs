using System.Text.Json;
using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public static class BridgeJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options);

    public static T? Deserialize<T>(JsonElement element) =>
        element.Deserialize<T>(Options);

    public static BridgeCommand? DeserializeCommand(string json) =>
        Deserialize<BridgeCommand>(json);

    public static BridgeCommand? DeserializeCommand(JsonElement element) =>
        Deserialize<BridgeCommand>(element);

    public static SqlExecutePayload? DeserializeSqlExecutePayload(JsonElement payload) =>
        Deserialize<SqlExecutePayload>(payload);

    public static QueryPackageExecutePayload? DeserializeQueryPackageExecutePayload(JsonElement payload) =>
        Deserialize<QueryPackageExecutePayload>(payload);

    public static DatabaseTestPayload? DeserializeDatabaseTestPayload(JsonElement payload) =>
        Deserialize<DatabaseTestPayload>(payload);

    public static DatabaseListPayload? DeserializeDatabaseListPayload(JsonElement payload) =>
        Deserialize<DatabaseListPayload>(payload);

    public static ChangesProbePayload? DeserializeChangesProbePayload(JsonElement payload) =>
        Deserialize<ChangesProbePayload>(payload);

    public static ChangesPullPayload? DeserializeChangesPullPayload(JsonElement payload) =>
        Deserialize<ChangesPullPayload>(payload);

    public static MarketingProductMovementPayload? DeserializeMarketingProductMovementPayload(JsonElement payload) =>
        Deserialize<MarketingProductMovementPayload>(payload);

    public static InfinityProductMovementPayload? DeserializeInfinityProductMovementPayload(JsonElement payload) =>
        Deserialize<InfinityProductMovementPayload>(payload);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed record DatabaseTestPayload
{
    [JsonPropertyName("databaseProfile")]
    public required string DatabaseProfile { get; init; }
}

public sealed record DatabaseListPayload
{
    [JsonPropertyName("databaseProfile")]
    public required string DatabaseProfile { get; init; }
}

/// <summary>
/// Phase 0/1 change-stream foundation. Identifies one watched domain, e.g.
/// system="marketing", domain="debt_invoice_events". KnownRevision lets the
/// phone tell the bridge what it last saw so changes.probe can answer
/// changed=true/false without re-sending the whole cursor.
/// </summary>
public sealed record ChangeDomainKey
{
    [JsonPropertyName("system")]
    public required string System { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("knownRevision")]
    public long? KnownRevision { get; init; }
}

/// <summary>
/// Empty/omitted Domains means "report every known domain". Cheap: never
/// touches SQL Server, only reads the local ChangeCursorStore.
/// </summary>
public sealed record ChangesProbePayload
{
    [JsonPropertyName("domains")]
    public List<ChangeDomainKey>? Domains { get; init; }
}

/// <summary>
/// Same shape as changes.probe today; reserved for once a cloud ticket
/// queue exists so the phone can actually pull queued deltas by domain.
/// </summary>
public sealed record ChangesPullPayload
{
    [JsonPropertyName("domains")]
    public List<ChangeDomainKey>? Domains { get; init; }
}

public sealed record MarketingProductMovementPayload
{
    [JsonPropertyName("productId")]
    public int ProductId { get; init; }

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime EndDate { get; init; }

    [JsonPropertyName("granularity")]
    public required string Granularity { get; init; }
}

public sealed record InfinityProductMovementPayload
{
    [JsonPropertyName("productId")]
    public int ProductId { get; init; }

    /// <summary>
    /// Preferred product lookup for the legacy named contract. It follows the
    /// Infinity product movement reference: exact barcode, exact product code,
    /// or partial product/short name. ProductId remains only as a compatibility
    /// fallback while older phones migrate to query.execute.
    /// </summary>
    [JsonPropertyName("searchTerm")]
    public string? SearchTerm { get; init; }

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime EndDate { get; init; }

    [JsonPropertyName("granularity")]
    public required string Granularity { get; init; }
}
