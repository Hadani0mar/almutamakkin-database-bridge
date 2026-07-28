using Almutamakkin.BarcodeBridge.Windows;

namespace Almutamakkin.BarcodeBridge.Tests;

public sealed class FirewallConfiguratorTests
{
    [Fact]
    public void FirewallRule_IsRestrictedToPrivateLan()
    {
        Assert.Contains("192.168.0.0", FirewallConfigurator.PrivateLanRemoteAddresses);
        Assert.Contains("10.0.0.0", FirewallConfigurator.PrivateLanRemoteAddresses);
        Assert.Contains("LocalSubnet", FirewallConfigurator.PrivateLanRemoteAddresses);
        Assert.DoesNotContain("100.64.0.0", FirewallConfigurator.PrivateLanRemoteAddresses);
        Assert.Contains("LAN", FirewallConfigurator.RuleName);
        Assert.DoesNotContain("Tailscale", FirewallConfigurator.RuleName);
    }
}
