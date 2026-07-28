using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class PermissionPolicy : IPermissionPolicy
{
    public PermissionCheckResult Evaluate(
        DatabaseProfile profile,
        string sql,
        QueryClassification classification)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.PermissionLevel switch
        {
            SqlPermissionLevel.ReadOnly => EvaluateReadOnly(sql, classification),
            SqlPermissionLevel.ReadWrite => EvaluateReadWrite(classification),
            SqlPermissionLevel.FullAccess => PermissionCheckResult.Allowed(),
            SqlPermissionLevel.Custom => EvaluateCustom(profile.CustomPermissions, sql, classification),
            _ => PermissionCheckResult.Denied("مستوى الصلاحية غير معروف."),
        };
    }

    /// <summary>
    /// Allows read queries (SELECT/WITH/SET/DECLARE/EXEC) and session-local
    /// analysis batches that use #temp / ##temp / @table variables
    /// (CREATE/INSERT/DROP/SELECT INTO against those only).
    /// Rejects permanent INSERT/UPDATE/DELETE/MERGE/TRUNCATE/SELECT INTO,
    /// permanent schema changes, and administrative commands.
    /// </summary>
    private static PermissionCheckResult EvaluateReadOnly(
        string sql,
        QueryClassification classification)
    {
        if (QueryClassifier.ContainsForbiddenDataChange(sql) ||
            classification == QueryClassification.Write)
        {
            return PermissionCheckResult.Denied(
                "غير مسموح: إضافة أو تعديل أو حذف البيانات (INSERT/UPDATE/DELETE).");
        }

        if (QueryClassifier.ContainsPermanentSchemaChange(sql) ||
            classification == QueryClassification.Schema)
        {
            return PermissionCheckResult.Denied(
                "ملف الاتصال يسمح بالاستعلامات دون أوامر المخطط أو الإدارية.");
        }

        if (classification == QueryClassification.Administrative)
        {
            return PermissionCheckResult.Denied(
                "ملف الاتصال يسمح بالاستعلامات دون أوامر المخطط أو الإدارية.");
        }

        // Read and Unknown (harmless / temp-table analysis batches) are allowed.
        return PermissionCheckResult.Allowed();
    }

    private static PermissionCheckResult EvaluateReadWrite(QueryClassification classification) =>
        classification switch
        {
            QueryClassification.Read or QueryClassification.Write =>
                PermissionCheckResult.Allowed(),
            QueryClassification.Schema or QueryClassification.Administrative =>
                PermissionCheckResult.Denied("ملف الاتصال لا يسمح بأوامر المخطط أو الإدارية."),
            _ => PermissionCheckResult.Denied("تعذر تصنيف الاستعلام أو أنه غير مسموح."),
        };

    private static PermissionCheckResult EvaluateCustom(
        CustomPermissionOptions options,
        string sql,
        QueryClassification classification)
    {
        if (classification == QueryClassification.Read)
        {
            return options.AllowRead
                ? PermissionCheckResult.Allowed()
                : PermissionCheckResult.Denied("الصلاحيات المخصصة لا تسمح باستعلامات القراءة.");
        }

        if (classification == QueryClassification.Schema)
        {
            return options.AllowSchemaChanges
                ? PermissionCheckResult.Allowed()
                : PermissionCheckResult.Denied("الصلاحيات المخصصة لا تسمح بتغييرات المخطط.");
        }

        if (classification == QueryClassification.Administrative)
        {
            return options.AllowAdministrativeCommands
                ? PermissionCheckResult.Allowed()
                : PermissionCheckResult.Denied("الصلاحيات المخصصة لا تسمح بالأوامر الإدارية.");
        }

        if (classification == QueryClassification.Write)
        {
            var keyword = QueryClassifier.ExtractFirstKeyword(sql);
            return keyword?.ToUpperInvariant() switch
            {
                "INSERT" when options.AllowInsert => PermissionCheckResult.Allowed(),
                "UPDATE" when options.AllowUpdate => PermissionCheckResult.Allowed(),
                "DELETE" when options.AllowDelete => PermissionCheckResult.Allowed(),
                "MERGE" when options.AllowUpdate || options.AllowInsert || options.AllowDelete =>
                    PermissionCheckResult.Allowed(),
                "EXEC" or "EXECUTE" when options.AllowExecuteProcedure =>
                    PermissionCheckResult.Allowed(),
                _ => PermissionCheckResult.Denied("الصلاحيات المخصصة لا تسمح بهذا النوع من أوامر الكتابة."),
            };
        }

        if (classification == QueryClassification.Unknown)
        {
            var keyword = QueryClassifier.ExtractFirstKeyword(sql);
            if (keyword is "EXEC" or "EXECUTE")
            {
                return options.AllowExecuteProcedure
                    ? PermissionCheckResult.Allowed()
                    : PermissionCheckResult.Denied("الصلاحيات المخصصة لا تسمح بتنفيذ الإجراءات المخزنة.");
            }

            return options.AllowRead
                ? PermissionCheckResult.Allowed()
                : PermissionCheckResult.Denied("الصلاحيات المخصصة لا تسمح بهذا الاستعلام.");
        }

        return PermissionCheckResult.Denied("تعذر تصنيف الاستعلام أو أنه غير مسموح.");
    }
}
