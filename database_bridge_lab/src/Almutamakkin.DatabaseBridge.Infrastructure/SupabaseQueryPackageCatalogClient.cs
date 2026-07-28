using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

/// <summary>
/// Downloads a decrypted package only after authenticating this bridge device.
/// SQL is kept in memory for a short interval and is never written to the
/// command queue, app settings, or bridge log.
/// </summary>
public sealed class SupabaseQueryPackageCatalogClient : IQueryPackageCatalogClient, IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly AppSettings _settings;
    private readonly ISecretProtector _secretProtector;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SupabaseQueryPackageCatalogClient(AppSettings settings, ISecretProtector secretProtector)
    {
        _settings = settings;
        _secretProtector = secretProtector;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<SignedQueryPackage?> GetAsync(string queryId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(queryId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
        {
            return cached.Package;
        }

        if (string.IsNullOrWhiteSpace(_settings.TunnelId) || string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            throw new InvalidOperationException("Query package catalog requires a registered bridge device.");
        }

        var baseUrl = SupabaseCloudConnectivity.ResolveFunctionsBaseUrl(_settings.SupabaseUrl);
        var requestUri = $"{baseUrl}/bridge-query-package";
        var body = BridgeJson.Serialize(new { tunnelId = _settings.TunnelId, queryId });
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        var anonKey = string.IsNullOrWhiteSpace(_settings.AnonKey)
            ? SupabaseBridgeTransport.DefaultAnonKey
            : _settings.AnonKey;
        request.Headers.Add("apikey", anonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", anonKey);
        request.Headers.Add("x-bridge-secret", _secretProtector.Unprotect(_settings.EncryptedDeviceSecret));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Query package catalog failed ({(int)response.StatusCode}).");
        }

        var payload = BridgeJson.Deserialize<QueryPackageCatalogResponse>(json);
        var package = payload?.Package;
        if (package is null)
        {
            return null;
        }

        var ttl = Math.Clamp(payload!.CacheSeconds, 30, 900);
        _cache[queryId] = new CacheEntry(package, DateTime.UtcNow.AddSeconds(ttl));
        return package;
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record CacheEntry(SignedQueryPackage Package, DateTime ExpiresAtUtc);

    private sealed record QueryPackageCatalogResponse
    {
        public SignedQueryPackage? Package { get; init; }

        public int CacheSeconds { get; init; } = 300;
    }
}

public sealed class RsaQueryPackageSignatureVerifier : IQueryPackageSignatureVerifier
{
    private readonly AppSettings _settings;

    public RsaQueryPackageSignatureVerifier(AppSettings settings) => _settings = settings;

    public bool Verify(SignedQueryPackage package, out string? errorMessage)
    {
        var publicKey = string.IsNullOrWhiteSpace(_settings.QueryPackageSigningPublicKeyPem)
            ? QueryPackageTrustAnchor.PublicKeyPem
            : _settings.QueryPackageSigningPublicKeyPem;

        if (!string.IsNullOrWhiteSpace(_settings.QueryPackageSigningKeyId) &&
            !string.Equals(_settings.QueryPackageSigningKeyId, package.KeyId, StringComparison.Ordinal))
        {
            errorMessage = "Unexpected query package signing key.";
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);
            var signature = Convert.FromBase64String(package.SignatureBase64);
            var valid = rsa.VerifyData(
                QueryPackageSignaturePayload.Build(package.Definition),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
            errorMessage = valid ? null : "Invalid query package signature.";
            return valid;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            errorMessage = "Invalid query package signature material.";
            return false;
        }
    }
}
