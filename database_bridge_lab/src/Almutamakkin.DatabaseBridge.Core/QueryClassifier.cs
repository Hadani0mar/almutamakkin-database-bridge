using System.Text;
using System.Text.RegularExpressions;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed partial class QueryClassifier : IQueryClassifier
{
    private static readonly HashSet<string> ReadKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT",
        "WITH",
        "SET",
        "DECLARE",
        "EXEC",
        "EXECUTE",
    };

    private static readonly HashSet<string> WriteKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT",
        "UPDATE",
        "DELETE",
        "MERGE",
    };

    private static readonly HashSet<string> SchemaKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CREATE",
        "ALTER",
        "DROP",
        "TRUNCATE",
    };

    private static readonly HashSet<string> AdministrativeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "BACKUP",
        "RESTORE",
        "DBCC",
        "SHUTDOWN",
        "KILL",
        "RECONFIGURE",
    };

    public QueryClassification Classify(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return QueryClassification.Unknown;
        }

        // Permanent data changes are always Write, even inside batches that
        // begin with SET / DECLARE / WITH. Session-local #temp / @table work is
        // not treated as a forbidden write.
        if (ContainsForbiddenDataChange(sql))
        {
            return QueryClassification.Write;
        }

        if (ContainsPermanentSchemaChange(sql))
        {
            return QueryClassification.Schema;
        }

        var keyword = ExtractFirstKeyword(sql);
        if (keyword is null)
        {
            return QueryClassification.Unknown;
        }

        if (ReadKeywords.Contains(keyword))
        {
            return QueryClassification.Read;
        }

        // INSERT/UPDATE/DELETE/MERGE into #temp or @table only → analysis batch.
        if (WriteKeywords.Contains(keyword))
        {
            return QueryClassification.Read;
        }

        // CREATE/DROP TABLE #temp only → analysis batch (permanent schema already handled).
        if (SchemaKeywords.Contains(keyword))
        {
            return QueryClassification.Read;
        }

        if (AdministrativeKeywords.Contains(keyword))
        {
            return QueryClassification.Administrative;
        }

        return QueryClassification.Unknown;
    }

    /// <summary>
    /// True when the SQL mutates permanent (non-temp) data: INSERT / UPDATE /
    /// DELETE / MERGE / TRUNCATE / SELECT…INTO against a non-session-local object.
    /// Session-local targets (#temp, ##temp, @table) are allowed.
    /// </summary>
    public static bool ContainsForbiddenDataChange(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

        var normalized = MaskLiteralsAndComments(sql);
        foreach (var statement in SplitStatements(normalized))
        {
            if (StatementHasPermanentMutation(statement))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the SQL creates/alters/drops permanent schema objects.
    /// CREATE/DROP TABLE #temp (and ##temp) are excluded.
    /// </summary>
    public static bool ContainsPermanentSchemaChange(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

        var normalized = MaskLiteralsAndComments(sql);
        foreach (var statement in SplitStatements(normalized))
        {
            if (StatementHasPermanentSchemaChange(statement))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="objectName"/> is a session-local temp table or
    /// table variable (#t, ##t, @t, tempdb..#t, [ #t ]).
    /// </summary>
    public static bool IsSessionLocalObject(string? objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        var name = objectName.Trim();
        name = name.TrimStart('[').TrimEnd(']');
        name = TempdbPrefixRegex().Replace(name, string.Empty);
        name = name.TrimStart('[').TrimEnd(']').Trim();

        if (name.StartsWith('@'))
        {
            return true;
        }

        return name.Contains('#', StringComparison.Ordinal);
    }

    internal static string? ExtractFirstKeyword(string sql)
    {
        var normalized = StripComments(sql).TrimStart();
        if (normalized.Length == 0)
        {
            return null;
        }

        // First keyword is enough for SET/DECLARE classified as Read.
        var match = FirstKeywordRegex().Match(normalized);
        return match.Success ? match.Value : null;
    }

    internal static string StripComments(string sql)
    {
        var withoutBlockComments = BlockCommentRegex().Replace(sql, " ");
        return LineCommentRegex().Replace(withoutBlockComments, " ");
    }

    internal static string MaskLiteralsAndComments(string sql)
    {
        var stripped = StripComments(sql);
        var masked = new StringBuilder(stripped.Length);
        var inLiteral = false;

        for (var index = 0; index < stripped.Length; index++)
        {
            var current = stripped[index];
            var next = index + 1 < stripped.Length ? stripped[index + 1] : '\0';

            if (inLiteral)
            {
                if (current == '\'' && next == '\'')
                {
                    masked.Append(' ');
                    masked.Append(' ');
                    index++;
                    continue;
                }

                if (current == '\'')
                {
                    inLiteral = false;
                }

                masked.Append(' ');
                continue;
            }

            if (current == '\'')
            {
                inLiteral = true;
                masked.Append(' ');
                continue;
            }

            masked.Append(current);
        }

        return masked.ToString();
    }

    private static IEnumerable<string> SplitStatements(string normalizedSql)
    {
        foreach (var part in normalizedSql.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    private static bool StatementHasPermanentMutation(string statement)
    {
        if (InsertTargetRegex().IsMatch(statement))
        {
            var target = InsertTargetRegex().Match(statement).Groups["t"].Value;
            if (!IsSessionLocalObject(target))
            {
                return true;
            }
        }

        if (UpdateTargetRegex().IsMatch(statement))
        {
            var target = UpdateTargetRegex().Match(statement).Groups["t"].Value;
            if (!IsSessionLocalObject(target))
            {
                return true;
            }
        }

        if (DeleteTargetRegex().IsMatch(statement))
        {
            var match = DeleteTargetRegex().Match(statement);
            // DELETE without an explicit target (rare) is treated as permanent.
            if (!match.Groups["t"].Success || !IsSessionLocalObject(match.Groups["t"].Value))
            {
                return true;
            }
        }

        if (MergeTargetRegex().IsMatch(statement))
        {
            var target = MergeTargetRegex().Match(statement).Groups["t"].Value;
            if (!IsSessionLocalObject(target))
            {
                return true;
            }
        }

        if (TruncateTargetRegex().IsMatch(statement))
        {
            var target = TruncateTargetRegex().Match(statement).Groups["t"].Value;
            if (!IsSessionLocalObject(target))
            {
                return true;
            }
        }

        // SELECT … INTO (not INSERT INTO): same statement starts with SELECT/WITH.
        if (SelectIntoTargetRegex().IsMatch(statement))
        {
            var target = SelectIntoTargetRegex().Match(statement).Groups["t"].Value;
            if (!IsSessionLocalObject(target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementHasPermanentSchemaChange(string statement)
    {
        if (AlterKeywordRegex().IsMatch(statement))
        {
            return true;
        }

        // Non-table CREATE/DROP (VIEW, PROC, INDEX, …) is always permanent schema.
        if (CreateNonTableRegex().IsMatch(statement) || DropNonTableRegex().IsMatch(statement))
        {
            return true;
        }

        if (CreateTableTargetRegex().IsMatch(statement))
        {
            var target = CreateTableTargetRegex().Match(statement).Groups["t"].Value;
            if (!IsSessionLocalObject(target))
            {
                return true;
            }
        }

        if (DropTableTargetRegex().IsMatch(statement))
        {
            var target = DropTableTargetRegex().Match(statement).Groups["t"].Value;
            if (!IsSessionLocalObject(target))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"--[^\r\n]*")]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex FirstKeywordRegex();

    [GeneratedRegex(
        @"^(?:tempdb\s*\.\s*(?:dbo\s*\.\s*)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TempdbPrefixRegex();

    [GeneratedRegex(
        @"\bINSERT\s+(?:INTO\s+)?(?<t>\[[^\]]+\]|[^\s;()]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InsertTargetRegex();

    [GeneratedRegex(
        @"\bUPDATE\s+(?<t>\[[^\]]+\]|[^\s;()]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpdateTargetRegex();

    [GeneratedRegex(
        @"\bDELETE\s+(?:TOP\s*\([^)]*\)\s+)?(?:FROM\s+)?(?<t>\[[^\]]+\]|[^\s;()]+)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeleteTargetRegex();

    [GeneratedRegex(
        @"\bMERGE\s+(?:INTO\s+)?(?<t>\[[^\]]+\]|[^\s;()]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MergeTargetRegex();

    [GeneratedRegex(
        @"\bTRUNCATE\s+TABLE\s+(?<t>\[[^\]]+\]|[^\s;()]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TruncateTargetRegex();

    [GeneratedRegex(
        @"^\s*(?:WITH\b[\s\S]*?\bAS\b[\s\S]*?)?\s*SELECT\b[\s\S]*?\bINTO\s+(?<t>\[[^\]]+\]|[^\s;()]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelectIntoTargetRegex();

    [GeneratedRegex(
        @"\bALTER\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlterKeywordRegex();

    [GeneratedRegex(
        @"\bCREATE\s+(?!TABLE\b)\w+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateNonTableRegex();

    [GeneratedRegex(
        @"\bDROP\s+(?!TABLE\b)\w+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DropNonTableRegex();

    [GeneratedRegex(
        @"\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<t>\[[^\]]+\]|[^\s;()]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateTableTargetRegex();

    [GeneratedRegex(
        @"\bDROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?<t>\[[^\]]+\]|[^\s;()]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DropTableTargetRegex();
}
