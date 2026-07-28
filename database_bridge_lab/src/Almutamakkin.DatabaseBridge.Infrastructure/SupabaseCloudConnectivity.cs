using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

/// <summary>
/// Preflight checks for Supabase cloud reachability (DNS + HTTPS) and Arabic operator messages.
/// </summary>
public static class SupabaseCloudConnectivity
{
    public const string DefaultHost = "mapfattjpsuizvlklddl.supabase.co";

    public sealed record CheckResult(
        bool Success,
        string Host,
        string MessageAr,
        string? TechnicalDetail);

    public static string ResolveFunctionsBaseUrl(string? supabaseFunctionsBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(supabaseFunctionsBaseUrl))
        {
            return SupabaseBridgeTransport.DefaultSupabaseFunctionsBaseUrl;
        }

        return supabaseFunctionsBaseUrl.Trim().TrimEnd('/');
    }

    public static string ExtractHost(string? supabaseFunctionsBaseUrl)
    {
        var raw = ResolveFunctionsBaseUrl(supabaseFunctionsBaseUrl);

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return DefaultHost;
        }

        return uri.Host;
    }

    public static async Task<CheckResult> CheckAsync(
        string? supabaseFunctionsBaseUrl,
        string? anonKey,
        CancellationToken cancellationToken)
    {
        var host = ExtractHost(supabaseFunctionsBaseUrl);
        var baseUrl = ResolveFunctionsBaseUrl(supabaseFunctionsBaseUrl);

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
                .ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                return FailDns(host, "DNS returned no addresses.");
            }
        }
        catch (SocketException ex)
        {
            return FailDns(host, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FailDns(host, Flatten(ex));
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            var key = string.IsNullOrWhiteSpace(anonKey)
                ? SupabaseBridgeTransport.DefaultAnonKey
                : anonKey.Trim();

            // Prefer HEAD against the functions base; fall back to GET if the edge rejects HEAD.
            HttpResponseMessage response;
            try
            {
                response = await SendProbeAsync(
                        client,
                        HttpMethod.Head,
                        baseUrl,
                        key,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                response = await SendProbeAsync(
                        client,
                        HttpMethod.Get,
                        baseUrl,
                        key,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            using (response)
            {
                // Any HTTP response means DNS + TLS + routing succeeded.
                return new CheckResult(
                    true,
                    host,
                    "الاتصال بسحابة الجسر ناجح.",
                    $"HTTP {(int)response.StatusCode}");
            }
        }
        catch (TaskCanceledException ex)
        {
            return new CheckResult(
                false,
                host,
                "انتهت مهلة الاتصال بخادم الجسر.\n\n" +
                "تحقق من الإنترنت أو الوكيل (Proxy) أو جدار الحماية.",
                Flatten(ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var detail = Flatten(ex);
            if (IsDnsFailure(ex) || IsDnsFailure(detail))
            {
                return FailDns(host, detail);
            }

            return new CheckResult(
                false,
                host,
                "تعذر الوصول لسحابة الجسر عبر HTTPS.\n\n" +
                "تحقق من:\n" +
                "• اتصال الإنترنت على هذا الجهاز\n" +
                "• جدار الحماية / مضاد الفيروسات (السماح للمنفذ 443)\n" +
                "• إعدادات البروكسي إن وُجدت\n" +
                $"• إمكانية فتح https://{host} من المتصفح",
                detail);
        }
    }

    public static string FormatUserMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsDnsFailure(exception) || IsDnsFailure(Flatten(exception)))
        {
            var detail = Flatten(exception);
            var host = TryExtractHostFromMessage(detail) ?? DefaultHost;
            return FailDns(host, detail).MessageAr;
        }

        if (IsTimeout(exception))
        {
            return
                "انتهت مهلة الاتصال بخادم الجسر.\n\n" +
                "تحقق من الإنترنت أو الوكيل (Proxy) أو جدار الحماية.\n\n" +
                $"تفاصيل: {SensitiveDataSanitizer.Sanitize(Flatten(exception))}";
        }

        return
            "فشل الاتصال بسحابة الجسر.\n\n" +
            "تأكد أن الجهاز متصل بالإنترنت ويمكنه الوصول إلى supabase.co\n\n" +
            $"تفاصيل: {SensitiveDataSanitizer.Sanitize(Flatten(exception))}";
    }

    public static bool IsDnsFailure(Exception exception)
    {
        foreach (var ex in Enumerate(exception))
        {
            if (ex is SocketException socketEx &&
                socketEx.SocketErrorCode is SocketError.HostNotFound
                    or SocketError.NoData
                    or SocketError.TryAgain)
            {
                return true;
            }

            if (IsDnsFailure(ex.Message))
            {
                return true;
            }
        }

        return false;
    }

    public static void TryOpenNetworkSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:network-status",
                UseShellExecute = true,
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ncpa.cpl",
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Best-effort only.
            }
        }
    }

    private static async Task<HttpResponseMessage> SendProbeAsync(
        HttpClient client,
        HttpMethod method,
        string baseUrl,
        string anonKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, baseUrl);
        request.Headers.TryAddWithoutValidation("apikey", anonKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {anonKey}");

        return await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private static CheckResult FailDns(string host, string technical)
    {
        return new CheckResult(
            false,
            host,
            "تعذر العثور على خادم سحابة الجسر (DNS).\n\n" +
            $"المضيف: {host}\n\n" +
            "هذا الجهاز لا يستطيع ترجمة اسم الخادم إلى عنوان IP.\n\n" +
            "الحلول الشائعة:\n" +
            "1) تأكد من اتصال الإنترنت\n" +
            "2) جرّب DNS عام: 8.8.8.8 أو 1.1.1.1 من إعدادات الشبكة\n" +
            "3) عطّل VPN مؤقتاً إن كان يمنع الأسماء الخارجية\n" +
            "4) افتح من المتصفح: https://" + host + "\n" +
            "5) إن كنت على شبكة شركة/مدرسة، اطلب السماح لـ *.supabase.co\n\n" +
            "الخطأ الإنجليزي الشائع: No such host is known.",
            technical);
    }

    private static bool IsDnsFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)
               || message.Contains("nodename nor servname", StringComparison.OrdinalIgnoreCase)
               || message.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase)
               || message.Contains("The remote name could not be resolved", StringComparison.OrdinalIgnoreCase)
               || message.Contains("getaddrinfo", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTimeout(Exception exception)
    {
        foreach (var ex in Enumerate(exception))
        {
            if (ex is TimeoutException or TaskCanceledException)
            {
                return true;
            }

            if (ex is SocketException socketEx &&
                socketEx.SocketErrorCode == SocketError.TimedOut)
            {
                return true;
            }

            var message = ex.Message ?? string.Empty;
            if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryExtractHostFromMessage(string message)
    {
        var start = message.IndexOf('(');
        var end = message.IndexOf(')');
        if (start < 0 || end <= start)
        {
            return null;
        }

        var inside = message.Substring(start + 1, end - start - 1);
        var colon = inside.IndexOf(':');
        return colon > 0 ? inside[..colon] : inside;
    }

    private static string Flatten(Exception exception)
    {
        var parts = new List<string>();
        foreach (var current in Enumerate(exception))
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                parts.Add(current.Message);
            }
        }

        return string.Join(" | ", parts);
    }

    private static IEnumerable<Exception> Enumerate(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                foreach (var item in Enumerate(inner))
                {
                    yield return item;
                }
            }

            yield break;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
