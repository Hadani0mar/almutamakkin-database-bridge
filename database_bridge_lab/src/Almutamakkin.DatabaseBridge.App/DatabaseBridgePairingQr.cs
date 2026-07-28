using System.Text.Json;
using System.Text.Json.Serialization;
using QRCoder;

namespace Almutamakkin.DatabaseBridge.App;

/// <summary>
/// Builds AMDB1 pairing payloads for QR display (mirror of barcode AMKB1).
/// </summary>
public static class DatabaseBridgePairingQr
{
    public const string Prefix = "AMDB1:";

    public static string CreatePayload(string pairingCode, string? tunnelId)
    {
        var code = (pairingCode ?? string.Empty).Trim().ToUpperInvariant();
        var tunnel = (tunnelId ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(tunnel))
        {
            throw new ArgumentException("Pairing code or tunnel id is required.");
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(new PairingPayload(1, code, tunnel));
        return Prefix + Base64UrlEncode(json);
    }

    public static Bitmap? CreateQrBitmap(string pairingCode, string? tunnelId, int pixelsPerModule = 8)
    {
        if (string.IsNullOrWhiteSpace(pairingCode) && string.IsNullOrWhiteSpace(tunnelId))
        {
            return null;
        }

        var payload = CreatePayload(pairingCode, tunnelId);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qr = new QRCode(data);
        return qr.GetGraphic(pixelsPerModule, Color.Black, Color.White, drawQuietZones: true);
    }

    internal static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record PairingPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("tunnelId")] string TunnelId);
}
