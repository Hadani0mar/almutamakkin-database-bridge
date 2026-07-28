using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Almutamakkin.BarcodeBridge.Configuration;

namespace Almutamakkin.BarcodeBridge.Pairing;

public static class PairingCodeService
{
    public const string Prefix = "AMKB1:";

    public static string Create(BridgeSettings settings, IPAddress lanAddress, string machineName)
    {
        var payload = new PairingPayload(
            1,
            $"http://{lanAddress}:{settings.Port}",
            settings.ApiKey,
            machineName,
            settings.PrinterName);
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Prefix + Base64UrlEncode(json);
    }

    internal static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }

    public static PairingPayload Decode(string code)
    {
        if (!code.StartsWith(Prefix, StringComparison.Ordinal))
            throw new FormatException("Pairing code prefix is invalid.");
        return JsonSerializer.Deserialize<PairingPayload>(Base64UrlDecode(code[Prefix.Length..]))
            ?? throw new FormatException("Pairing payload is empty.");
    }
}

public sealed record PairingPayload(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("apiKey")] string ApiKey,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("printer")] string Printer);
