using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

public sealed class SupabaseSnapshotIngestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(3),
    };

    public async Task<SnapshotIngestResult> PublishAsync(
        AppSettings settings,
        string deviceSecret,
        SnapshotIngestRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSecret);
        ArgumentNullException.ThrowIfNull(request);

        var baseUrl = SupabaseCloudConnectivity.ResolveFunctionsBaseUrl(settings.SupabaseUrl);
        var anonKey = string.IsNullOrWhiteSpace(settings.AnonKey)
            ? SupabaseBridgeTransport.DefaultAnonKey
            : settings.AnonKey!;

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/bridge-snapshot-ingest");
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {anonKey}");
        httpRequest.Headers.TryAddWithoutValidation("apikey", anonKey);
        httpRequest.Headers.TryAddWithoutValidation("x-bridge-secret", deviceSecret);

        var json = JsonSerializer.Serialize(request, JsonOptions);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new SnapshotIngestResult(
                Success: false,
                SnapshotId: null,
                RowCount: 0,
                Message: $"ingest failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;
        return new SnapshotIngestResult(
            Success: root.TryGetProperty("success", out var ok) && ok.GetBoolean(),
            SnapshotId: root.TryGetProperty("snapshotId", out var id) ? id.GetString() : null,
            RowCount: root.TryGetProperty("rowCount", out var count) ? count.GetInt32() : 0,
            Message: body);
    }
}

public sealed record SnapshotIngestRequest
{
    public required string TunnelId { get; init; }
    public required string System { get; init; }
    public required string SnapshotType { get; init; }
    public string CalculationVersion { get; init; } = "1";
    public Dictionary<string, object?> Params { get; init; } = new();
    public string GeneratedAt { get; init; } = DateTime.UtcNow.ToString("O");
    public List<Dictionary<string, object?>> Rows { get; init; } = new();
    public List<Dictionary<string, object?>>? Headers { get; init; }
}

public sealed record SnapshotIngestResult(
    bool Success,
    string? SnapshotId,
    int RowCount,
    string Message);
