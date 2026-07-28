using System.Diagnostics;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;
using Microsoft.Data.SqlClient;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class SqlCommandExecutor : ISqlCommandExecutor
{
    private readonly ISecretProtector _secretProtector;
    private readonly IConnectionStringBuilder _connectionStringBuilder;

    public SqlCommandExecutor(
        ISecretProtector secretProtector,
        IConnectionStringBuilder connectionStringBuilder)
    {
        _secretProtector = secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));
        _connectionStringBuilder = connectionStringBuilder
            ?? throw new ArgumentNullException(nameof(connectionStringBuilder));
    }

    public async Task<SqlExecutionResult> ExecuteAsync(
        DatabaseProfile profile,
        SqlExecutePayload request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var plainPassword = ResolvePlainPassword(profile);
            var connectionString = _connectionStringBuilder.Build(profile, plainPassword);
            var commandTimeoutSeconds = ResolveCommandTimeout(request, profile);
            var maxRows = ResolveMaxRows(request, profile);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = request.Sql;
            command.CommandTimeout = commandTimeoutSeconds;
            SqlParameterBinder.AddParameters(command, request.Parameters);

            await using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    command.Cancel();
                }
                catch
                {
                    // Ignore cancellation races while closing the command.
                }
            });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            var resultSets = new List<SqlResultSet>();
            var totalReturnedRows = 0;
            var wasTruncated = false;
            var affectedRows = -1;
            var resultSetIndex = 0;
            var remainingRows = maxRows;

            do
            {
                if (reader.FieldCount == 0)
                {
                    affectedRows = Math.Max(affectedRows, reader.RecordsAffected);
                    resultSetIndex++;
                    continue;
                }

                var rawColumnNames = Enumerable.Range(0, reader.FieldCount)
                    .Select(reader.GetName)
                    .ToArray();
                var columnNames = SqlValueConverter.BuildUniqueColumnNames(rawColumnNames);
                var columns = BuildColumnDefinitions(reader, columnNames);
                var rows = new List<Dictionary<string, object?>>();
                var resultSetTruncated = false;

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (remainingRows <= 0)
                    {
                        wasTruncated = true;
                        resultSetTruncated = true;
                        break;
                    }

                    var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                    for (var columnIndex = 0; columnIndex < columnNames.Count; columnIndex++)
                    {
                        row[columnNames[columnIndex]] = SqlValueConverter.ConvertValue(
                            reader.GetValue(columnIndex));
                    }

                    rows.Add(row);
                    totalReturnedRows++;
                    remainingRows--;
                }

                resultSets.Add(new SqlResultSet
                {
                    Index = resultSetIndex,
                    Columns = columns,
                    Rows = rows,
                    WasTruncated = resultSetTruncated,
                });

                affectedRows = Math.Max(affectedRows, reader.RecordsAffected);
                resultSetIndex++;
            }
            while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

            stopwatch.Stop();

            return new SqlExecutionResult
            {
                Success = true,
                ResultSets = resultSets,
                AffectedRows = affectedRows,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                WasTruncated = wasTruncated,
                TotalReturnedRows = totalReturnedRows,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Failure(
                ErrorCodes.SqlExecutionFailed,
                "تم إلغاء تنفيذ الاستعلام.",
                stopwatch.ElapsedMilliseconds);
        }
        catch (SqlException ex) when (IsTimeout(ex))
        {
            stopwatch.Stop();
            return Failure(
                ErrorCodes.SqlTimeout,
                "انتهت مهلة تنفيذ الاستعلام.",
                stopwatch.ElapsedMilliseconds);
        }
        catch (SqlException ex)
        {
            stopwatch.Stop();
            return Failure(
                ErrorCodes.SqlExecutionFailed,
                SanitizeExecutionError(ex),
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Failure(
                ErrorCodes.SqlExecutionFailed,
                SanitizeExecutionError(ex),
                stopwatch.ElapsedMilliseconds);
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

    private static int ResolveCommandTimeout(SqlExecutePayload request, DatabaseProfile profile)
    {
        // Prefer the caller-requested timeout for heavy analysis batches; fall
        // back to the profile default when the request omits a timeout.
        var requestedTimeout = request.TimeoutSeconds > 0
            ? request.TimeoutSeconds
            : 0;
        var profileTimeout = profile.CommandTimeoutSeconds > 0
            ? profile.CommandTimeoutSeconds
            : BridgeLimits.DefaultTimeoutSeconds;
        var effective = requestedTimeout > 0 ? requestedTimeout : profileTimeout;

        return Math.Clamp(effective, 1, BridgeLimits.MaximumTimeoutSeconds);
    }

    private static int ResolveMaxRows(SqlExecutePayload request, DatabaseProfile profile)
    {
        var requestedMaxRows = request.MaxRows > 0
            ? request.MaxRows
            : BridgeLimits.DefaultMaxRows;
        var profileMaxRows = profile.MaximumRows > 0
            ? profile.MaximumRows
            : BridgeLimits.DefaultMaxRows;

        // Snapshot sync passes an explicit MaxRows above the interactive profile cap.
        var effective = request.MaxRows > profileMaxRows
            ? requestedMaxRows
            : Math.Min(requestedMaxRows, profileMaxRows);

        return Math.Clamp(effective, 1, BridgeLimits.MaximumMaxRows);
    }

    private static List<SqlColumnDefinition> BuildColumnDefinitions(
        SqlDataReader reader,
        IReadOnlyList<string> columnNames)
    {
        var columns = new List<SqlColumnDefinition>(columnNames.Count);

        for (var columnIndex = 0; columnIndex < columnNames.Count; columnIndex++)
        {
            columns.Add(new SqlColumnDefinition
            {
                Name = columnNames[columnIndex],
                DataType = reader.GetFieldType(columnIndex)?.Name ?? "Object",
                AllowNull = true,
            });
        }

        return columns;
    }

    private static bool IsTimeout(SqlException exception) =>
        exception.Number == -2
        || exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeExecutionError(Exception exception)
    {
        var message = exception.Message;
        if (exception is SqlException sqlException && sqlException.Errors.Count > 0)
        {
            message = sqlException.Errors[0].Message;
        }

        return SensitiveDataSanitizer.Sanitize(message);
    }

    private static SqlExecutionResult Failure(
        string errorCode,
        string errorMessage,
        long executionTimeMs) =>
        new()
        {
            Success = false,
            ExecutionTimeMs = executionTimeMs,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
        };
}
