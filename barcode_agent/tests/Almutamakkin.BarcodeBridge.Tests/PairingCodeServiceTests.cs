using System.Net;
using Almutamakkin.BarcodeBridge.Configuration;
using Almutamakkin.BarcodeBridge.Pairing;

namespace Almutamakkin.BarcodeBridge.Tests;

public sealed class PairingCodeServiceTests
{
    [Fact]
    public void Create_UsesVersionedBase64UrlContract()
    {
        var settings = new BridgeSettings
        {
            Port = 8787,
            ApiKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG",
            PrinterName = "Xprinter XP-365B Raw"
        };

        var code = PairingCodeService.Create(settings, IPAddress.Parse("192.168.1.50"), "PHARMACY-PC");
        var payload = PairingCodeService.Decode(code);

        Assert.StartsWith("AMKB1:", code);
        Assert.DoesNotContain('+', code[PairingCodeService.Prefix.Length..]);
        Assert.DoesNotContain('/', code[PairingCodeService.Prefix.Length..]);
        Assert.DoesNotContain('=', code[PairingCodeService.Prefix.Length..]);
        Assert.Equal(1, payload.Version);
        Assert.Equal("http://192.168.1.50:8787", payload.Url);
        Assert.Equal(settings.ApiKey, payload.ApiKey);
        Assert.Equal("PHARMACY-PC", payload.Name);
        Assert.Equal(settings.PrinterName, payload.Printer);
    }
}
