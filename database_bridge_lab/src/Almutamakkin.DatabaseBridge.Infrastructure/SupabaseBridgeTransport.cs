using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class SupabaseBridgeTransport : ICommandTransport, IAsyncDisposable
{
    public const string DefaultSupabaseFunctionsBaseUrl =
        "https://mapfattjpsuizvlklddl.supabase.co/functions/v1";

    public const string DefaultAnonKey =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im1hcGZhdHRqcHN1aXp2bGtsZGRsIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODA3Nzg3OTMsImV4cCI6MjA5NjM1NDc5M30.V9gWxS2gIqhPaYH6aaEGKphtcRUlqp4IUQNUgKS0h5k";

    private const int PollWaitMs = 20_000;
    private const int PollLimit = 3;

    private readonly AppSettings _settings;
    private readonly ISecretProtector _secretProtector;
    private readonly HttpClient _httpClient;
    private readonly object _sync = new();

    private CancellationTokenSource? _pollLoopCts;
    private Task? _pollLoopTask;
    private bool _isConnected;
    private string? _lastPollStatus;
    private DateTime? _lastPollAtUtc;

    public SupabaseBridgeTransport(AppSettings settings, ISecretProtector secretProtector)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _secretProtector = secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(PollWaitMs + 15),
        };
    }

    public event Func<BridgeCommand, CancellationToken, Task>? CommandReceived;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _isConnected;
            }
        }
    }

    public string? LastPollStatus
    {
        get
        {
            lock (_sync)
            {
                return _lastPollStatus;
            }
        }
    }

    public DateTime? LastPollAtUtc
    {
        get
        {
            lock (_sync)
            {
                return _lastPollAtUtc;
            }
        }
    }

    public static async Task<BridgeRegisterResult> RegisterAsync(
        string? supabaseFunctionsBaseUrl,
        string? anonKey,
        CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient(anonKey);
        var baseUrl = ResolveFunctionsBaseUrl(supabaseFunctionsBaseUrl);
        var requestUri = $"{baseUrl}/bridge-register";

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await httpClient
            .PostAsync(requestUri, content, cancellationToken)
            .ConfigureAwait(false);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"bridge-register failed ({(int)response.StatusCode}): {responseBody}");
        }

        var result = BridgeJson.Deserialize<BridgeRegisterResult>(responseBody)
            ?? throw new InvalidOperationException("Unable to deserialize bridge-register response.");

        if (string.IsNullOrWhiteSpace(result.TunnelId)
            || string.IsNullOrWhiteSpace(result.DeviceSecret))
        {
            throw new InvalidOperationException(
                "bridge-register response is missing tunnelId or deviceSecret.");
        }

        return result;
    }

    public async Task<BridgeRegisterResult> RefreshPairingAsync(
        string tunnelId,
        string deviceSecret,
        string? supabaseFunctionsBaseUrl,
        string? anonKey,
        bool rotateCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tunnelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSecret);

        using var httpClient = CreateHttpClient(anonKey);
        var baseUrl = ResolveFunctionsBaseUrl(supabaseFunctionsBaseUrl);
        var requestUri = $"{baseUrl}/bridge-refresh-pairing";
        var body = BridgeJson.Serialize(new BridgeRefreshPairingRequest
        {
            TunnelId = tunnelId.Trim().ToUpperInvariant(),
            RotateCode = rotateCode,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("x-bridge-secret", deviceSecret);
        ApplyAnonHeaders(request, ResolveAnonKey(anonKey));

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"bridge-refresh-pairing failed ({(int)response.StatusCode}): {responseBody}");
        }

        var result = BridgeJson.Deserialize<BridgeRegisterResult>(responseBody)
            ?? throw new InvalidOperationException(
                "Unable to deserialize bridge-refresh-pairing response.");

        if (string.IsNullOrWhiteSpace(result.PairingCode))
        {
            throw new InvalidOperationException(
                "bridge-refresh-pairing response is missing pairingCode.");
        }

        return result;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);

        var pollLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        lock (_sync)
        {
            _isConnected = true;
            _pollLoopCts = pollLoopCts;
            _pollLoopTask = Task.Run(
                () => PollLoopAsync(pollLoopCts.Token),
                CancellationToken.None);
        }

        SetPollStatus("Connected — waiting for commands");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        Task? pollLoopTask;
        CancellationTokenSource? pollLoopCts;

        lock (_sync)
        {
            pollLoopTask = _pollLoopTask;
            pollLoopCts = _pollLoopCts;
            _pollLoopTask = null;
            _pollLoopCts = null;
            _isConnected = false;
        }

        if (pollLoopCts is not null)
        {
            await pollLoopCts.CancelAsync().ConfigureAwait(false);
            pollLoopCts.Dispose();
        }

        if (pollLoopTask is not null)
        {
            try
            {
                await pollLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when shutting down the poll loop.
            }
        }

        SetPollStatus("Disconnected");
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task SendResponseAsync(BridgeResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!IsConnected)
        {
            throw new InvalidOperationException("Supabase tunnel transport is offline.");
        }

        var baseUrl = ResolveFunctionsBaseUrl(_settings.SupabaseUrl);
        var requestUri = $"{baseUrl}/bridge-respond";
        var body = BridgeJson.Serialize(new BridgeRespondRequest
        {
            TunnelId = _settings.TunnelId,
            RequestId = response.RequestId,
            Response = response,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        ApplyBridgeSecretHeader(request, GetDeviceSecret());
        ApplyAnonHeaders(request, ResolveAnonKey(_settings.AnonKey));

        using var httpResponse = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"bridge-respond failed ({(int)httpResponse.StatusCode}): {responseBody}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _httpClient.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!IsConnected)
            {
                break;
            }

            try
            {
                SetPollStatus("Polling…");
                var commands = await PollOnceAsync(cancellationToken).ConfigureAwait(false);
                SetPollStatus(
                    commands.Count == 0
                        ? $"Idle @ {DateTime.UtcNow:HH:mm:ss} UTC"
                        : $"Received {commands.Count} command(s) @ {DateTime.UtcNow:HH:mm:ss} UTC");

                foreach (var command in commands)
                {
                    var handlers = CommandReceived;
                    if (handlers is null)
                    {
                        continue;
                    }

                    // Do not block the next poll while SQL runs — queue work in the background.
                    var captured = command;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            foreach (var handler in handlers.GetInvocationList()
                                         .Cast<Func<BridgeCommand, CancellationToken, Task>>())
                            {
                                await handler(captured, CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            SetPollStatus($"Command error: {ex.Message}");
                        }
                    }, CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetPollStatus($"Poll error: {SupabaseCloudConnectivity.FormatUserMessage(ex)}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task<IReadOnlyList<BridgeCommand>> PollOnceAsync(CancellationToken cancellationToken)
    {
        var baseUrl = ResolveFunctionsBaseUrl(_settings.SupabaseUrl);
        var requestUri = $"{baseUrl}/bridge-poll";
        var body = BridgeJson.Serialize(new BridgePollRequest
        {
            TunnelId = _settings.TunnelId,
            WaitMs = PollWaitMs,
            Limit = PollLimit,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        ApplyBridgeSecretHeader(request, GetDeviceSecret());
        ApplyAnonHeaders(request, ResolveAnonKey(_settings.AnonKey));

        using var httpResponse = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"bridge-poll failed ({(int)httpResponse.StatusCode}): {responseBody}");
        }

        var pollResponse = BridgeJson.Deserialize<BridgePollResponse>(responseBody)
            ?? new BridgePollResponse();

        if (pollResponse.Commands is null || pollResponse.Commands.Count == 0)
        {
            return Array.Empty<BridgeCommand>();
        }

        var commands = new List<BridgeCommand>(pollResponse.Commands.Count);
        foreach (var commandElement in pollResponse.Commands)
        {
            var command = BridgeJson.DeserializeCommand(commandElement);
            if (command is not null)
            {
                commands.Add(command);
            }
        }

        return commands;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.TunnelId))
        {
            throw new InvalidOperationException(
                "Supabase tunnel is not configured. Register the bridge device first.");
        }

        if (string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            throw new InvalidOperationException(
                "Supabase tunnel device secret is missing. Register the bridge device first.");
        }
    }

    private string GetDeviceSecret() =>
        _secretProtector.Unprotect(_settings.EncryptedDeviceSecret!);

    private void SetPollStatus(string status)
    {
        lock (_sync)
        {
            _lastPollStatus = status;
            _lastPollAtUtc = DateTime.UtcNow;
        }
    }

    private static string ResolveFunctionsBaseUrl(string? configuredUrl) =>
        SupabaseCloudConnectivity.ResolveFunctionsBaseUrl(configuredUrl);

    private static string ResolveAnonKey(string? configuredAnonKey) =>
        string.IsNullOrWhiteSpace(configuredAnonKey)
            ? DefaultAnonKey
            : configuredAnonKey;

    private static HttpClient CreateHttpClient(string? anonKey)
    {
        var client = new HttpClient();
        ApplyAnonHeaders(client, ResolveAnonKey(anonKey));
        return client;
    }

    private static void ApplyAnonHeaders(HttpClient client, string anonKey)
    {
        client.DefaultRequestHeaders.Remove("apikey");
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Add("apikey", anonKey);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anonKey);
    }

    private static void ApplyAnonHeaders(HttpRequestMessage request, string anonKey)
    {
        request.Headers.Remove("apikey");
        request.Headers.Remove("Authorization");
        request.Headers.Add("apikey", anonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", anonKey);
    }

    private static void ApplyBridgeSecretHeader(HttpRequestMessage request, string deviceSecret) =>
        request.Headers.Add("x-bridge-secret", deviceSecret);

    private sealed record BridgePollRequest
    {
        [JsonPropertyName("tunnelId")]
        public required string TunnelId { get; init; }

        [JsonPropertyName("waitMs")]
        public required int WaitMs { get; init; }

        [JsonPropertyName("limit")]
        public required int Limit { get; init; }
    }

    private sealed record BridgePollResponse
    {
        [JsonPropertyName("commands")]
        public List<System.Text.Json.JsonElement>? Commands { get; init; }
    }

    private sealed record BridgeRespondRequest
    {
        [JsonPropertyName("tunnelId")]
        public required string TunnelId { get; init; }

        [JsonPropertyName("requestId")]
        public required string RequestId { get; init; }

        [JsonPropertyName("response")]
        public required BridgeResponse Response { get; init; }
    }

    private sealed record BridgeRefreshPairingRequest
    {
        [JsonPropertyName("tunnelId")]
        public required string TunnelId { get; init; }

        [JsonPropertyName("rotateCode")]
        public bool RotateCode { get; init; }
    }
}

public sealed record BridgeRegisterResult
{
    [JsonPropertyName("tunnelId")]
    public string? TunnelId { get; init; }

    [JsonPropertyName("pairingCode")]
    public string? PairingCode { get; init; }

    [JsonPropertyName("pairingExpiresAt")]
    public string? PairingExpiresAt { get; init; }

    [JsonPropertyName("deviceSecret")]
    public string? DeviceSecret { get; init; }
}
