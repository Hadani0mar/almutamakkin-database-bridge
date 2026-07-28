using System.Security.Cryptography;
using System.Text;
using Almutamakkin.DatabaseBridge.Core;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("Almutamakkin.DatabaseBridgeLab");

    public string Protect(string plainText)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainText);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(
            plainBytes,
            OptionalEntropy,
            DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string protectedText)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedText);

        var protectedBytes = Convert.FromBase64String(protectedText);
        var plainBytes = ProtectedData.Unprotect(
            protectedBytes,
            OptionalEntropy,
            DataProtectionScope.CurrentUser);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
