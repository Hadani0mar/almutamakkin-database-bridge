using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

public sealed class SupabaseLiveIngestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public async Task<SnapshotIngestResult> PublishActiveInvoicesAsync(
        AppSettings settings,
        string deviceSecret,
        string system,
        IReadOnlyList<Dictionary<string, object?>> invoices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSecret);

        var baseUrl = SupabaseCloudConnectivity.ResolveFunctionsBaseUrl(settings.SupabaseUrl);
        var anonKey = string.IsNullOrWhiteSpace(settings.AnonKey)
            ? SupabaseBridgeTransport.DefaultAnonKey
            : settings.AnonKey!;

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/bridge-live-ingest");
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {anonKey}");
        httpRequest.Headers.TryAddWithoutValidation("apikey", anonKey);
        httpRequest.Headers.TryAddWithoutValidation("x-bridge-secret", deviceSecret);

        var payload = new
        {
            tunnelId = settings.TunnelId,
            system,
            channel = "active_invoices",
            generatedAt = DateTime.UtcNow.ToString("O"),
            invoices,
        };
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new SnapshotIngestResult(
                false,
                null,
                0,
                $"live ingest failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;
        return new SnapshotIngestResult(
            root.TryGetProperty("success", out var ok) && ok.GetBoolean(),
            null,
            root.TryGetProperty("rowCount", out var count) ? count.GetInt32() : invoices.Count,
            body);
    }
}
