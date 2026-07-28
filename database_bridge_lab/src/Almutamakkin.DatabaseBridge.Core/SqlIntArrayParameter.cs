using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// Expands a package-only integer list into individually bound SQL parameters.
/// SQL Server has no portable scalar-list parameter, so a package uses a
/// placeholder such as <c>IN (@employeeIds)</c>. Each integer remains bound;
/// only generated parameter names are written into SQL text.
/// </summary>
public static partial class SqlIntArrayParameter
{
    public static bool TryRead(object? value, out IReadOnlyList<int> values, out string? error)
    {
        if (value is JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Array)
            {
                values = Array.Empty<int>();
                error = "قيمة قائمة الأرقام يجب أن تكون مصفوفة.";
                return false;
            }

            var parsed = new List<int>();
            foreach (var item in json.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var number))
                {
                    values = Array.Empty<int>();
                    error = "قائمة الأرقام تقبل أرقاماً صحيحة فقط.";
                    return false;
                }

                parsed.Add(number);
                if (parsed.Count > BridgeLimits.MaximumIntArrayParameterItems)
                {
                    values = Array.Empty<int>();
                    error = $"قائمة الأرقام تتجاوز الحد {BridgeLimits.MaximumIntArrayParameterItems}.";
                    return false;
                }
            }

            values = parsed;
            error = null;
            return true;
        }

        if (value is IEnumerable<int> numbers)
        {
            var parsed = new List<int>();
            foreach (var number in numbers)
            {
                parsed.Add(number);
                if (parsed.Count > BridgeLimits.MaximumIntArrayParameterItems)
                {
                    values = Array.Empty<int>();
                    error = $"قائمة الأرقام تتجاوز الحد {BridgeLimits.MaximumIntArrayParameterItems}.";
                    return false;
                }
            }

            values = parsed;
            error = null;
            return true;
        }

        values = Array.Empty<int>();
        error = "قيمة قائمة الأرقام يجب أن تكون مصفوفة أرقام صحيحة.";
        return false;
    }

    public static SqlExecutePayload Expand(SqlExecutePayload request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sql = request.Sql;
        var expanded = new Dictionary<string, SqlParameterValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, parameter) in request.Parameters)
        {
            if (!string.Equals(parameter.Type, "int[]", StringComparison.OrdinalIgnoreCase))
            {
                expanded.Add(name, parameter);
                continue;
            }

            if (!TryRead(parameter.Value, out var values, out var valueError))
            {
                throw new ArgumentException(valueError ?? "Invalid int[] parameter value.", name);
            }

            var normalizedName = NormalizeParameterName(name);
            var generatedNames = new List<string>(values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                var generatedName = $"{normalizedName}__{index}";
                generatedNames.Add($"@{generatedName}");
                expanded.Add(generatedName, new SqlParameterValue { Type = "int", Value = values[index] });
            }

            // An empty IN list is invalid SQL. NULL keeps IN (@ids) valid and
            // deterministically returns no rows without introducing a sentinel.
            var replacement = generatedNames.Count == 0 ? "NULL" : string.Join(", ", generatedNames);
            var replacements = ReplaceParameterTokens(sql, normalizedName, replacement, out var rewrittenSql);
            if (replacements == 0)
            {
                throw new ArgumentException(
                    $"The signed package does not reference parameter '@{normalizedName}'.",
                    name);
            }

            sql = rewrittenSql;
        }

        return request with { Sql = sql, Parameters = expanded };
    }

    private static string NormalizeParameterName(string name)
    {
        var normalized = name.Trim().TrimStart('@');
        if (!ParameterNameRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Array parameter names must use letters, digits, and underscores.", name);
        }

        return normalized;
    }

    private static int ReplaceParameterTokens(
        string sql,
        string parameterName,
        string replacement,
        out string rewrittenSql)
    {
        var output = new StringBuilder(sql.Length + replacement.Length);
        var replacements = 0;
        var index = 0;
        while (index < sql.Length)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (current == '\'')
            {
                output.Append(current);
                index++;
                while (index < sql.Length)
                {
                    output.Append(sql[index]);
                    if (sql[index] == '\'' && index + 1 < sql.Length && sql[index + 1] == '\'')
                    {
                        output.Append(sql[index + 1]);
                        index += 2;
                        continue;
                    }

                    if (sql[index++] == '\'')
                    {
                        break;
                    }
                }
                continue;
            }

            if (current == '[')
            {
                output.Append(current);
                index++;
                while (index < sql.Length)
                {
                    output.Append(sql[index]);
                    if (sql[index] == ']' && index + 1 < sql.Length && sql[index + 1] == ']')
                    {
                        output.Append(sql[index + 1]);
                        index += 2;
                        continue;
                    }

                    if (sql[index++] == ']')
                    {
                        break;
                    }
                }
                continue;
            }

            if (current == '-' && next == '-')
            {
                output.Append(current).Append(next);
                index += 2;
                while (index < sql.Length && sql[index] is not '\r' and not '\n')
                {
                    output.Append(sql[index++]);
                }
                continue;
            }

            if (current == '/' && next == '*')
            {
                output.Append(current).Append(next);
                index += 2;
                while (index < sql.Length)
                {
                    output.Append(sql[index]);
                    if (sql[index] == '*' && index + 1 < sql.Length && sql[index + 1] == '/')
                    {
                        output.Append(sql[index + 1]);
                        index += 2;
                        break;
                    }
                    index++;
                }
                continue;
            }

            if (current == '@' && (index == 0 || sql[index - 1] != '@'))
            {
                var tokenStart = index + 1;
                var tokenEnd = tokenStart;
                while (tokenEnd < sql.Length && IsParameterNameCharacter(sql[tokenEnd]))
                {
                    tokenEnd++;
                }

                if (tokenEnd > tokenStart &&
                    string.Equals(sql[tokenStart..tokenEnd], parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    output.Append(replacement);
                    index = tokenEnd;
                    replacements++;
                    continue;
                }
            }

            output.Append(current);
            index++;
        }

        rewrittenSql = output.ToString();
        return replacements;
    }

    private static bool IsParameterNameCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNameRegex();
}
