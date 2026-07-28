using System.Net;
using System.Security.Cryptography;
using System.Text;
using Almutamakkin.BarcodeAgent.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;

namespace Almutamakkin.BarcodeAgent.Security;

public sealed class ApiSecurityMiddleware(
    RequestDelegate next,
    IOptions<SecurityOptions> options,
    ILogger<ApiSecurityMiddleware> logger)
{
    private readonly SecurityOptions _options = options.Value;
    private readonly IReadOnlyList<IpNetwork> _networks = options.Value.AllowedNetworks
        .Select(IpNetwork.Parse)
        .ToArray();

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null || !_networks.Any(network => network.Contains(remoteIp)))
        {
            logger.LogWarning("Rejected API request from disallowed address {RemoteIp}", remoteIp);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Network access denied"
            });
            return;
        }

        var candidate = context.Request.Headers[_options.HeaderName].FirstOrDefault();
        if (!FixedTimeEquals(candidate, _options.ApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid API key"
            });
            return;
        }

        await next(context);
    }

    internal static bool FixedTimeEquals(string? candidate, string expected)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length != expected.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(expected));
    }
}

internal sealed record IpNetwork(IPAddress Address, int PrefixLength)
{
    public static IpNetwork Parse(string value)
    {
        var parts = value.Split('/', 2);
        var address = IPAddress.Parse(parts[0]);
        var prefix = parts.Length == 2 ? int.Parse(parts[1]) : address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        var max = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefix is < 0 || prefix > 128 || prefix > max) throw new FormatException($"Invalid CIDR: {value}");
        return new IpNetwork(address, prefix);
    }

    public bool Contains(IPAddress candidate)
    {
        if (candidate.IsIPv4MappedToIPv6) candidate = candidate.MapToIPv4();
        var networkAddress = Address.IsIPv4MappedToIPv6 ? Address.MapToIPv4() : Address;
        if (candidate.AddressFamily != networkAddress.AddressFamily) return false;
        var networkBytes = networkAddress.GetAddressBytes();
        var candidateBytes = candidate.GetAddressBytes();
        var fullBytes = PrefixLength / 8;
        var remainingBits = PrefixLength % 8;
        for (var i = 0; i < fullBytes; i++)
            if (networkBytes[i] != candidateBytes[i]) return false;
        if (remainingBits == 0) return true;
        var mask = (byte)(0xFF << (8 - remainingBits));
        return (networkBytes[fullBytes] & mask) == (candidateBytes[fullBytes] & mask);
    }
}
