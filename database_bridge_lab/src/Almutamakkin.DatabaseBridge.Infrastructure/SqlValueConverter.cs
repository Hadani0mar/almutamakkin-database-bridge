using System.Globalization;
using System.Text.Json;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public static class SqlValueConverter
{
    private const int MaxInlineBinaryBytes = 4096;

    public static object? ConvertValue(object? value) => ToJsonValue(value);

    public static object? ToJsonValue(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        return value switch
        {
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            byte[] bytes => ConvertBinary(bytes),
            TimeSpan timeSpan => timeSpan.ToString("c", CultureInfo.InvariantCulture),
            bool or sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal or string =>
                value,
            _ => value.ToString(),
        };
    }

    public static object? ParseParameterValue(string type, object? rawValue)
    {
        if (type.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return DBNull.Value;
        }

        if (rawValue is JsonElement element)
        {
            return ParseFromJsonElement(type, element);
        }

        return type.ToLowerInvariant() switch
        {
            "string" => rawValue?.ToString(),
            "int" => Convert.ToInt32(rawValue, CultureInfo.InvariantCulture),
            "long" => Convert.ToInt64(rawValue, CultureInfo.InvariantCulture),
            "decimal" => Convert.ToDecimal(rawValue, CultureInfo.InvariantCulture),
            "double" => Convert.ToDouble(rawValue, CultureInfo.InvariantCulture),
            "bool" => Convert.ToBoolean(rawValue, CultureInfo.InvariantCulture),
            "datetime" => DateTime.Parse(rawValue?.ToString() ?? string.Empty, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            "guid" => Guid.Parse(rawValue?.ToString() ?? string.Empty),
            _ => rawValue,
        };
    }

    private static object? ParseFromJsonElement(string type, JsonElement element)
    {
        if (type.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return DBNull.Value;
        }

        return type.ToLowerInvariant() switch
        {
            "string" => element.GetString(),
            "int" => element.GetInt32(),
            "long" => element.GetInt64(),
            "decimal" => element.GetDecimal(),
            "double" => element.GetDouble(),
            "bool" => element.GetBoolean(),
            "datetime" => DateTime.Parse(element.GetString() ?? string.Empty, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            "guid" => Guid.Parse(element.GetString() ?? string.Empty),
            _ => element.ToString(),
        };
    }

    private static object ConvertBinary(byte[] bytes) =>
        bytes.Length <= MaxInlineBinaryBytes
            ? Convert.ToBase64String(bytes)
            : $"<binary:{bytes.Length} bytes omitted>";

    public static List<string> BuildUniqueColumnNames(IReadOnlyList<string> originalNames)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(originalNames.Count);

        foreach (var name in originalNames)
        {
            if (!counts.TryGetValue(name, out var count))
            {
                counts[name] = 1;
                result.Add(name);
                continue;
            }

            count++;
            counts[name] = count;
            result.Add($"{name}_{count}");
        }

        return result;
    }
}
