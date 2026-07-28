using System.Net;
using Almutamakkin.BarcodeBridge.Networking;

namespace Almutamakkin.BarcodeBridge.Tests;

public sealed class LanIpDetectorTests
{
    [Theory]
    [InlineData("192.168.1.10", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("100.98.0.86", false)]
    [InlineData("8.8.8.8", false)]
    public void IsUsableLanAddress_AcceptsPrivateRfc1918Only(string value, bool expected) =>
        Assert.Equal(expected, LanIpDetector.IsUsableLanAddress(IPAddress.Parse(value)));

    [Theory]
    [InlineData("100.64.0.0", true)]
    [InlineData("100.98.0.86", true)]
    [InlineData("192.168.1.1", false)]
    public void IsTailscaleAddress_StillRecognizesCgnat(string value, bool expected) =>
        Assert.Equal(expected, TailscaleIpDetector.IsTailscaleAddress(IPAddress.Parse(value)));
}
