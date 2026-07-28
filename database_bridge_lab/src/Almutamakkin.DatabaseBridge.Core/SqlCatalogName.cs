using System.Text.RegularExpressions;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// Validates optional Initial Catalog overrides from the phone.
/// Rejects only characters that could break a connection string.
/// </summary>
public static partial class SqlCatalogName
{
    public static bool TryNormalize(string? raw, out string normalized, out string? errorMessage)
    {
        normalized = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            errorMessage = "اسم القاعدة فارغ.";
            return false;
        }

        if (normalized.Length > 128)
        {
            errorMessage = "اسم القاعدة أطول من المسموح.";
            return false;
        }

        if (!ValidCatalogName().IsMatch(normalized))
        {
            errorMessage =
                "اسم القاعدة غير صالح. تجنب الرموز الخاصة مثل الفاصلة المنقوطة وعلامة التساوي.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    // Allow typical SQL Server database names; block connection-string metacharacters.
    [GeneratedRegex(@"^[^;=\r\n\x00\[\]]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidCatalogName();
}
