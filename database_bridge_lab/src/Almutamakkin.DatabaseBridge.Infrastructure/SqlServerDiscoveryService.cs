using System.Runtime.Versioning;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class SqlServerDiscoveryService : ISqlServerDiscovery
{
    public IReadOnlyList<SqlServerInstanceInfo> DiscoverLocalInstances()
    {
        var results = new List<SqlServerInstanceInfo>();
        var machine = Environment.MachineName;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
            if (key is not null)
            {
                foreach (var instanceName in key.GetValueNames())
                {
                    var isDefault = string.Equals(instanceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase);
                    var dataSource = isDefault ? "localhost" : $@".\{instanceName}";
                    var display = isDefault
                        ? $"{machine} (Default / localhost)"
                        : $"{machine}\\{instanceName}";

                    results.Add(new SqlServerInstanceInfo(
                        DisplayName: display,
                        DataSource: dataSource,
                        InstanceName: isDefault ? null : instanceName,
                        IsDefaultInstance: isDefault,
                        IsLocal: true));
                }
            }
        }
        catch
        {
            // Registry may be unavailable; fall through to defaults.
        }

        if (results.Count == 0)
        {
            results.Add(new SqlServerInstanceInfo(
                DisplayName: $"{machine} (localhost)",
                DataSource: "localhost",
                InstanceName: null,
                IsDefaultInstance: true,
                IsLocal: true));
        }

        // Always offer explicit localhost TCP-friendly alias first for default.
        if (!results.Any(item => string.Equals(item.DataSource, "localhost", StringComparison.OrdinalIgnoreCase)))
        {
            results.Insert(0, new SqlServerInstanceInfo(
                DisplayName: "localhost",
                DataSource: "localhost",
                InstanceName: null,
                IsDefaultInstance: true,
                IsLocal: true));
        }

        return results
            .GroupBy(item => item.DataSource, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.IsDefaultInstance)
            .ThenBy(item => item.DisplayName)
            .ToList();
    }

    public async Task<IReadOnlyList<SqlDatabaseInfo>> ListDatabasesAsync(
        string dataSource,
        SqlAuthenticationMode authenticationMode,
        string? userName,
        string? plainPassword,
        bool trustServerCertificate,
        bool encryptConnection,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new ArgumentException("Data source is required.", nameof(dataSource));
        }

        var normalized = SqlConnectionStringBuilderService.NormalizeDataSource(dataSource);
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = normalized,
            InitialCatalog = "master",
            TrustServerCertificate = trustServerCertificate,
            Encrypt = encryptConnection,
            ConnectTimeout = 8,
            ApplicationName = "Almutamakkin.DatabaseBridgeLab.Discovery",
        };

        if (authenticationMode == SqlAuthenticationMode.WindowsAuthentication)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.IntegratedSecurity = false;
            builder.UserID = userName ?? string.Empty;
            builder.Password = plainPassword ?? string.Empty;
        }

        var databases = new List<SqlDatabaseInfo>();

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM sys.databases
            WHERE state_desc = N'ONLINE'
              AND name NOT IN (N'tempdb')
            ORDER BY
              CASE
                WHEN name IN (N'Marketing', N'Marketing2026', N'InfinityRetailDB') THEN 0
                WHEN name IN (N'master', N'model', N'msdb') THEN 2
                ELSE 1
              END,
              name;
            """;
        command.CommandTimeout = 15;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            databases.Add(new SqlDatabaseInfo(name, SuggestBridgeProfileName(name)));
        }

        return databases;
    }

    public static string SuggestBridgeProfileName(string databaseName)
    {
        if (string.Equals(databaseName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase))
        {
            return "InfinityRetailDB";
        }

        if (databaseName.StartsWith("Marketing", StringComparison.OrdinalIgnoreCase))
        {
            // Exact Marketing stays the phone-facing canonical profile name.
            // Same-schema backups keep their own names so the desktop list can
            // distinguish them; the phone still routes via Marketing + catalog.
            return string.Equals(databaseName, "Marketing", StringComparison.OrdinalIgnoreCase)
                ? "Marketing"
                : databaseName;
        }

        return databaseName;
    }
}
