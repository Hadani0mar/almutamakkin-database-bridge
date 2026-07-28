using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

public sealed record ChangeTicketPublishResult(bool Attempted, bool Success, string Message);

/// <summary>
/// Publishes a change ticket (and optional empty delta shell) to
/// <c>bridge-change-publish</c>. Auth matches snapshot ingest:
/// <c>x-bridge-secret</c> against <c>bridge_devices</c>.
/// </summary>
public sealed class SupabaseChangeTicketClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(45),
    };

    private readonly IBridgeLogger _logger;

    public SupabaseChangeTicketClient(IBridgeLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ChangeTicketPublishResult> PublishAsync(
        AppSettings settings,
        string deviceSecret,
        string system,
        string domain,
        long revision,
        long? previousRevision = null,
        string? fingerprint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSecret);

        if (string.IsNullOrWhiteSpace(settings.TunnelId) ||
            string.IsNullOrWhiteSpace(settings.SupabaseUrl))
        {
            return new ChangeTicketPublishResult(
                Attempted: false,
                Success: false,
                Message: "نفق الجسر أو عنوان السحابة غير مهيأ.");
        }

        try
        {
            var baseUrl = SupabaseCloudConnectivity.ResolveFunctionsBaseUrl(settings.SupabaseUrl);
            var anonKey = string.IsNullOrWhiteSpace(settings.AnonKey)
                ? SupabaseBridgeTransport.DefaultAnonKey
                : settings.AnonKey!;

            var watermark = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                watermark["fingerprint"] = fingerprint;
            }

            var fromRevision = previousRevision ?? Math.Max(0, revision - 1);
            object? delta = null;
            if (revision > fromRevision)
            {
                delta = new
                {
                    fromRevision,
                    toRevision = revision,
                    contractVersion = 1,
                    complete = true,
                    cursor = watermark,
                    upserts = Array.Empty<object>(),
                    tombstones = Array.Empty<object>(),
                    reconcileRequired = false,
                };
            }

            var payload = new
            {
                tunnelId = settings.TunnelId.Trim().ToUpperInvariant(),
                system = system.Trim().ToLowerInvariant(),
                domain = domain.Trim(),
                ticket = new
                {
                    revision,
                    watermark,
                    changedKeyCount = 0,
                    hasTombstones = false,
                    deltaAvailable = delta is not null,
                    reconcileRequired = false,
                },
                delta,
            };

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/bridge-change-publish");
            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {anonKey}");
            httpRequest.Headers.TryAddWithoutValidation("apikey", anonKey);
            httpRequest.Headers.TryAddWithoutValidation("x-bridge-secret", deviceSecret);
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient
                .SendAsync(httpRequest, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning(
                    $"change-ticket publish failed ({(int)response.StatusCode}) " +
                    $"{system}/{domain} rev={revision}: {body}");
                return new ChangeTicketPublishResult(
                    Attempted: true,
                    Success: false,
                    Message: $"فشل النشر ({(int)response.StatusCode})");
            }

            _logger.Info($"change-ticket published: {system}/{domain} rev={revision}");
            return new ChangeTicketPublishResult(
                Attempted: true,
                Success: true,
                Message: "تم نشر تذكرة التغيير.");
        }
        catch (Exception ex)
        {
            _logger.Error($"change-ticket publish error: {ex.Message}", ex);
            return new ChangeTicketPublishResult(
                Attempted: true,
                Success: false,
                Message: ex.Message);
        }
    }
}
