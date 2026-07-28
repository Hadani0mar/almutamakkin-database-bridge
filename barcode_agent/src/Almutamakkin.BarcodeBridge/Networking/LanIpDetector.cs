using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Almutamakkin.BarcodeBridge.Networking;

/// <summary>
/// Picks a reachable LAN IPv4 for phone pairing (no Tailscale dependency).
/// </summary>
public static class LanIpDetector
{
    public static IPAddress? Detect()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(network =>
                    network.OperationalStatus == OperationalStatus.Up
                    && network.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                    && network.NetworkInterfaceType is not NetworkInterfaceType.Tunnel)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address =>
                    address.AddressFamily == AddressFamily.InterNetwork
                    && IsUsableLanAddress(address))
                .OrderBy(Score)
                .ThenBy(address => address.ToString(), StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    public static bool IsUsableLanAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        if (IPAddress.IsLoopback(address)) return false;

        var bytes = address.GetAddressBytes();
        // Link-local APIPA
        if (bytes[0] == 169 && bytes[1] == 254) return false;
        // Former Tailscale CGNAT — never require or prefer it
        if (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) return false;

        return IsPrivateRfc1918(bytes);
    }

    private static bool IsPrivateRfc1918(byte[] bytes) =>
        bytes[0] == 10
        || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
        || (bytes[0] == 192 && bytes[1] == 168);

    private static int Score(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        // Prefer typical home/office Wi‑Fi (192.168.*) then 10.* then 172.16–31.*
        if (bytes[0] == 192 && bytes[1] == 168) return 0;
        if (bytes[0] == 10) return 1;
        return 2;
    }
}
