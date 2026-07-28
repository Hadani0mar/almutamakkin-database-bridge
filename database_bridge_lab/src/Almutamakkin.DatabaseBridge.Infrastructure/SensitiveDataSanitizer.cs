using System.Text.RegularExpressions;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public static partial class SensitiveDataSanitizer
{
    [GeneratedRegex(@"(?i)(Password|Pwd)\s*=\s*[^;""']+", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringPasswordRegex();

    [GeneratedRegex(@"(?i)(User\s*ID|UID)\s*=\s*[^;""']+", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringUserRegex();

    public static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return message ?? string.Empty;
        }

        if (!LooksLikeConnectionString(message))
        {
            return message;
        }

        var sanitized = ConnectionStringPasswordRegex().Replace(message, "$1=***");
        sanitized = ConnectionStringUserRegex().Replace(sanitized, "$1=***");
        return sanitized;
    }

    public static bool LooksLikeConnectionString(string message) =>
        message.Contains("Password=", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Pwd=", StringComparison.OrdinalIgnoreCase)
        || message.Contains("User ID=", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Server=", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Data Source=", StringComparison.OrdinalIgnoreCase);
}
