using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;
using Microsoft.Data.SqlClient;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class SqlConnectionStringBuilderService : IConnectionStringBuilder
{
    public string Build(DatabaseProfile profile, string? plainPassword)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = NormalizeDataSource(profile.ServerName),
            InitialCatalog = profile.DatabaseName,
            Encrypt = profile.EncryptConnection,
            TrustServerCertificate = profile.TrustServerCertificate,
            ConnectTimeout = Math.Clamp(
                profile.CommandTimeoutSeconds > 0
                    ? Math.Min(profile.CommandTimeoutSeconds, 60)
                    : 15,
                3,
                60),
            ApplicationName = "Almutamakkin.DatabaseBridgeLab",
        };

        // Remote/Tailscale profiles drop mid-query more often; retry the
        // initial handshake without extending CommandTimeout itself.
        if (profile.ConnectionKind == DatabaseConnectionKind.Network)
        {
            builder.ConnectRetryCount = 3;
            builder.ConnectRetryInterval = 10;
        }

        if (profile.AuthenticationMode == SqlAuthenticationMode.WindowsAuthentication)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.IntegratedSecurity = false;
            builder.UserID = profile.UserName ?? string.Empty;
            builder.Password = plainPassword ?? string.Empty;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Prefer localhost for the default instance. Bare "." / "(local)" can fail with
    /// Shared Memory pipe errors on some machines; localhost is verified working here.
    /// Avoid forcing tcp:127.0.0.1 with Windows auth (untrusted domain on this host).
    /// </summary>
    public static string NormalizeDataSource(string? serverName)
    {
        var value = (serverName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "localhost";
        }

        if (value is "." or "(local)" or "(Local)" ||
            string.Equals(value, "local", StringComparison.OrdinalIgnoreCase))
        {
            return "localhost";
        }

        // ".\MSSQLSERVER" is invalid; default instance should be localhost.
        if (value.Equals(@".\MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith(@"\MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
        {
            return "localhost";
        }

        return value;
    }
}
