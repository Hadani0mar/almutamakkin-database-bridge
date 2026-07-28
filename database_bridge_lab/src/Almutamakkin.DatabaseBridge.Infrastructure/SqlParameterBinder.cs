using System.Data;
using System.Globalization;
using System.Text.Json;
using Almutamakkin.DatabaseBridge.Protocol;
using Microsoft.Data.SqlClient;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

internal static class SqlParameterBinder
{
    public static void AddParameters(SqlCommand command, IReadOnlyDictionary<string, SqlParameterValue> parameters)
    {
        foreach (var (name, parameter) in parameters)
        {
            var parameterName = name.StartsWith('@') ? name : $"@{name}";
            var sqlParameter = new SqlParameter(parameterName, ResolveDbType(parameter.Type))
            {
                Value = ResolveValue(parameter),
            };

            command.Parameters.Add(sqlParameter);
        }
    }

    private static SqlDbType ResolveDbType(string type) =>
        type.Trim().ToLowerInvariant() switch
        {
            "string" => SqlDbType.NVarChar,
            "int" => SqlDbType.Int,
            "long" => SqlDbType.BigInt,
            "decimal" => SqlDbType.Decimal,
            "double" => SqlDbType.Float,
            "bool" or "boolean" => SqlDbType.Bit,
            "datetime" => SqlDbType.DateTime2,
            "guid" => SqlDbType.UniqueIdentifier,
            "null" => SqlDbType.NVarChar,
            _ => throw new ArgumentException($"Unsupported SQL parameter type '{type}'."),
        };

    private static object ResolveValue(SqlParameterValue parameter)
    {
        if (string.Equals(parameter.Type, "null", StringComparison.OrdinalIgnoreCase)
            || parameter.Value is null)
        {
            return DBNull.Value;
        }

        if (parameter.Value is JsonElement jsonElement)
        {
            return ResolveJsonValue(parameter.Type, jsonElement);
        }

        return parameter.Type.Trim().ToLowerInvariant() switch
        {
            "string" => Convert.ToString(parameter.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            "int" => Convert.ToInt32(parameter.Value, CultureInfo.InvariantCulture),
            "long" => Convert.ToInt64(parameter.Value, CultureInfo.InvariantCulture),
            "decimal" => Convert.ToDecimal(parameter.Value, CultureInfo.InvariantCulture),
            "double" => Convert.ToDouble(parameter.Value, CultureInfo.InvariantCulture),
            "bool" or "boolean" => Convert.ToBoolean(parameter.Value, CultureInfo.InvariantCulture),
            "datetime" => parameter.Value is DateTime dateTime
                ? dateTime
                : DateTime.Parse(
                    Convert.ToString(parameter.Value, CultureInfo.InvariantCulture)
                    ?? throw new FormatException("Invalid datetime parameter value."),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
            "guid" => parameter.Value is Guid guid
                ? guid
                : Guid.Parse(Convert.ToString(parameter.Value, CultureInfo.InvariantCulture)
                    ?? throw new FormatException("Invalid guid parameter value.")),
            _ => parameter.Value,
        };
    }

    private static object ResolveJsonValue(string type, JsonElement jsonElement)
    {
        if (jsonElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return DBNull.Value;
        }

        return type.Trim().ToLowerInvariant() switch
        {
            "string" => jsonElement.GetString() ?? string.Empty,
            "int" => jsonElement.GetInt32(),
            "long" => jsonElement.GetInt64(),
            "decimal" => jsonElement.GetDecimal(),
            "double" => jsonElement.GetDouble(),
            "bool" or "boolean" => jsonElement.GetBoolean(),
            "datetime" => jsonElement.GetDateTime(),
            "guid" => jsonElement.GetGuid(),
            _ => jsonElement.ToString(),
        };
    }
}
