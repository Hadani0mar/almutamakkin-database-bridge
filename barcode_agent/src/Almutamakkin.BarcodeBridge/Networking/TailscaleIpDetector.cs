using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Almutamakkin.BarcodeBridge.Networking;

[Obsolete("Use LanIpDetector. Kept only for older test names.")]
public static class TailscaleIpDetector
{
    public static IPAddress? Detect() => LanIpDetector.Detect();

    public static bool IsTailscaleAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }
}
