using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;
using Microsoft.Data.SqlClient;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class SqlDatabaseConnectionTester : IDatabaseConnectionTester
{
    private const string TestQuery =
        """
        SELECT
            DB_NAME() AS DatabaseName,
            @@SERVERNAME AS ServerName,
            SUSER_SNAME() AS LoginName;
        """;

    private readonly ISecretProtector _secretProtector;
    private readonly IConnectionStringBuilder _connectionStringBuilder;

    public SqlDatabaseConnectionTester(
        ISecretProtector secretProtector,
        IConnectionStringBuilder connectionStringBuilder)
    {
        _secretProtector = secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));
        _connectionStringBuilder = connectionStringBuilder
            ?? throw new ArgumentNullException(nameof(connectionStringBuilder));
    }

    public async Task<DatabaseConnectionTestResult> TestAsync(
        DatabaseProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        try
        {
            var plainPassword = ResolvePlainPassword(profile);
            var connectionString = _connectionStringBuilder.Build(profile, plainPassword);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = TestQuery;
            command.CommandTimeout = Math.Clamp(
                profile.CommandTimeoutSeconds > 0
                    ? profile.CommandTimeoutSeconds
                    : BridgeLimits.DefaultTimeoutSeconds,
                1,
                BridgeLimits.MaximumTimeoutSeconds);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new DatabaseConnectionTestResult
                {
                    Success = false,
                    Message = "تعذر قراءة نتيجة اختبار الاتصال.",
                };
            }

            var databaseName = reader["DatabaseName"]?.ToString();
            var serverName = reader["ServerName"]?.ToString();
            var loginName = reader["LoginName"]?.ToString();

            return new DatabaseConnectionTestResult
            {
                Success = true,
                Message = "تم الاتصال بنجاح.",
                DatabaseName = databaseName,
                ServerName = serverName,
                LoginName = loginName,
                Details = $"Database={databaseName}; Server={serverName}; Login={loginName}",
            };
        }
        catch (SqlException ex)
        {
            return new DatabaseConnectionTestResult
            {
                Success = false,
                Message = "فشل الاتصال بقاعدة البيانات.",
                Details = SensitiveDataSanitizer.Sanitize(ex.Message),
            };
        }
        catch (Exception ex)
        {
            return new DatabaseConnectionTestResult
            {
                Success = false,
                Message = "حدث خطأ أثناء اختبار الاتصال.",
                Details = SensitiveDataSanitizer.Sanitize(ex.Message),
            };
        }
    }

    private string? ResolvePlainPassword(DatabaseProfile profile)
    {
        if (profile.AuthenticationMode == SqlAuthenticationMode.WindowsAuthentication)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(profile.EncryptedPassword))
        {
            throw new InvalidOperationException("SQL authentication requires an encrypted password.");
        }

        return _secretProtector.Unprotect(profile.EncryptedPassword);
    }
}
