using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Almutamakkin.BarcodeBridge.Windows;

public static class FirewallConfigurator
{
    public const string RuleName = "Almutamakkin Barcode Bridge (LAN)";
    /// <summary>Private RFC1918 ranges — no Tailscale.</summary>
    public const string PrivateLanRemoteAddresses =
        "10.0.0.0/255.0.0.0,172.16.0.0/255.240.0.0,192.168.0.0/255.255.0.0,LocalSubnet";

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static int ConfigureElevated(int port)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (!IsAdministrator()) throw new UnauthorizedAccessException("Administrator rights are required.");

        var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!;
        var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!;
        dynamic? policy = null;
        dynamic? rule = null;
        try
        {
            policy = Activator.CreateInstance(policyType)
                ?? throw new InvalidOperationException("Windows Firewall policy is unavailable.");
            // Remove legacy Tailscale-named rule and current rule if present.
            foreach (var name in new[] { RuleName, "Almutamakkin Barcode Bridge (Tailscale)" })
            {
                try { policy.Rules.Remove(name); } catch (COMException) { }
            }

            rule = Activator.CreateInstance(ruleType)
                ?? throw new InvalidOperationException("Windows Firewall rule creation failed.");
            rule.Name = RuleName;
            rule.Description = "Allows the Almutamakkin mobile app from private LAN addresses.";
            rule.ApplicationName = Environment.ProcessPath;
            rule.Protocol = 6; // TCP
            rule.LocalPorts = port.ToString();
            rule.RemoteAddresses = PrivateLanRemoteAddresses;
            rule.Direction = 1; // Inbound
            rule.Action = 1; // Allow
            rule.Enabled = true;
            rule.Profiles = 7; // Domain, private, public
            rule.EdgeTraversal = false;
            policy.Rules.Add(rule);
            return 0;
        }
        finally
        {
            if (rule is not null && Marshal.IsComObject(rule)) Marshal.FinalReleaseComObject(rule);
            if (policy is not null && Marshal.IsComObject(policy)) Marshal.FinalReleaseComObject(policy);
        }
    }

    public static async Task<int> RunElevatedAsync(int port, CancellationToken cancellationToken = default)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("تعذر تحديد مسار البرنامج.");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"--configure-firewall {port}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new Win32Exception("تعذر تشغيل إعداد جدار الحماية.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
