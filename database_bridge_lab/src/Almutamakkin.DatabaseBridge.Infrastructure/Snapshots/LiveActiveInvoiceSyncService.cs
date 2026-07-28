using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

/// <summary>
/// Publishes Marketing live open invoices from the local phone profile (not remote snapshot).
/// </summary>
public sealed class LiveActiveInvoiceSyncService
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly ISqlCommandExecutor _executor;
    private readonly ISecretProtector _secretProtector;
    private readonly AppSettings _settings;
    private readonly SupabaseLiveIngestClient _ingestClient;
    private readonly ISnapshotFingerprintStore _fingerprints;
    private readonly IBridgeLogger _logger;

    public LiveActiveInvoiceSyncService(
        IDatabaseProfileStore profileStore,
        ISqlCommandExecutor executor,
        ISecretProtector secretProtector,
        AppSettings settings,
        SupabaseLiveIngestClient ingestClient,
        ISnapshotFingerprintStore fingerprints,
        IBridgeLogger logger)
    {
        _profileStore = profileStore;
        _executor = executor;
        _secretProtector = secretProtector;
        _settings = settings;
        _ingestClient = ingestClient;
        _fingerprints = fingerprints;
        _logger = logger;
    }

    public async Task<SnapshotJobResult> PublishMarketingAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
            string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            return new SnapshotJobResult(
                "marketing",
                "active_invoices",
                false,
                0,
                "سجّل جهاز الجسر أولاً.");
        }

        var profile = ResolveMarketingProfileForLive();
        if (profile is null)
        {
            return new SnapshotJobResult(
                "marketing",
                "active_invoices",
                false,
                0,
                "لا يوجد ملف Marketing (محلي أو بعيد) لبث الفاتورة الحية.");
        }

        try
        {
            var deviceSecret = _secretProtector.Unprotect(_settings.EncryptedDeviceSecret!);
            var summarySql = SnapshotSqlFiles.Read("marketing_live_active_invoices.sql");
            var summaryExec = await _executor.ExecuteAsync(
                profile,
                new SqlExecutePayload
                {
                    DatabaseProfile = profile.ProfileName,
                    Sql = summarySql,
                    TimeoutSeconds = 30,
                    MaxRows = 50,
                },
                cancellationToken).ConfigureAwait(false);

            if (!summaryExec.Success)
            {
                return new SnapshotJobResult(
                    "marketing",
                    "active_invoices",
                    false,
                    0,
                    summaryExec.ErrorMessage ?? "فشل جلب الفواتير الحية.");
            }

            var summaries = summaryExec.ResultSets.FirstOrDefault()?.Rows
                ?? new List<Dictionary<string, object?>>();
            var fingerprint = HashSummaries(summaries);
            var previous = _fingerprints.Get("marketing", "active_invoices");
            if (previous is not null &&
                string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                _fingerprints.Set(
                    "marketing",
                    "active_invoices",
                    fingerprint,
                    DateTime.UtcNow,
                    rowCount: summaries.Count,
                    published: false);
                return new SnapshotJobResult(
                    "marketing",
                    "active_invoices",
                    true,
                    summaries.Count,
                    "لا تغيير في الفاتورة الحية — تخطي النشر.");
            }

            var invoiceIds = summaries
                .Select(row => ReadString(row, "invoice_id", "invoiceId"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var itemsByInvoice = new Dictionary<string, List<Dictionary<string, object?>>>(
                StringComparer.OrdinalIgnoreCase);
            if (invoiceIds.Count > 0)
            {
                var idsCsv = string.Join(",", invoiceIds.Select(id => id!.Replace("'", "''")));
                var itemsTemplate = SnapshotSqlFiles.Read("marketing_live_active_invoice_items.sql");
                var itemsSql = itemsTemplate.Replace("{{INVOICE_IDS}}", idsCsv, StringComparison.Ordinal);
                var itemsExec = await _executor.ExecuteAsync(
                    profile,
                    new SqlExecutePayload
                    {
                        DatabaseProfile = profile.ProfileName,
                        Sql = itemsSql,
                        TimeoutSeconds = 30,
                        MaxRows = 2000,
                    },
                    cancellationToken).ConfigureAwait(false);

                if (itemsExec.Success)
                {
                    var itemRows = itemsExec.ResultSets.FirstOrDefault()?.Rows
                        ?? new List<Dictionary<string, object?>>();
                    foreach (var row in itemRows)
                    {
                        var invoiceId = ReadString(row, "invoice_id", "invoiceId") ?? string.Empty;
                        if (!itemsByInvoice.TryGetValue(invoiceId, out var list))
                        {
                            list = new List<Dictionary<string, object?>>();
                            itemsByInvoice[invoiceId] = list;
                        }

                        list.Add(new Dictionary<string, object?>
                        {
                            ["itemId"] = ReadString(row, "item_id", "itemId"),
                            ["itemName"] = Sanitize(ReadString(row, "item_name", "itemName")) ?? "صنف",
                            ["qty"] = ReadNumber(row, "qty", "QTY"),
                            ["price"] = ReadNumber(row, "price", "PRICE"),
                            ["lineTotal"] = ReadNumber(row, "line_total", "lineTotal"),
                            ["unitFactor"] = ReadNumber(row, "unit_factor", "unitFactor"),
                            ["unitLabel"] = Sanitize(ReadString(row, "unit_label", "unitLabel")),
                        });
                    }
                }
            }

            var payload = new List<Dictionary<string, object?>>(summaries.Count);
            foreach (var row in summaries)
            {
                var invoiceId = ReadString(row, "invoice_id", "invoiceId") ?? string.Empty;
                itemsByInvoice.TryGetValue(invoiceId, out var items);
                payload.Add(new Dictionary<string, object?>
                {
                    ["invoiceId"] = invoiceId,
                    ["employeeId"] = ReadString(row, "employee_id", "employeeId"),
                    ["employeeName"] = Sanitize(ReadString(row, "employee_name", "employeeName"))
                        ?? "غير محدد",
                    ["customerId"] = ReadString(row, "customer_id", "customerId"),
                    ["invoiceKind"] = ReadString(row, "invoice_kind", "invoiceKind"),
                    ["invoiceLifecycle"] = ReadString(row, "invoice_lifecycle", "invoiceLifecycle")
                        ?? "live",
                    ["totalAmount"] = ReadNumber(row, "total_amount", "totalAmount") ?? 0,
                    ["lineCount"] = ReadNumber(row, "line_count", "lineCount") ?? 0,
                    ["startedAt"] = ReadString(row, "started_at", "startedAt"),
                    ["lastItemAt"] = ReadString(row, "last_item_at", "lastItemAt"),
                    ["items"] = items ?? new List<Dictionary<string, object?>>(),
                });
            }

            var publish = await _ingestClient.PublishActiveInvoicesAsync(
                _settings,
                deviceSecret,
                "marketing",
                payload,
                cancellationToken).ConfigureAwait(false);

            if (publish.Success)
            {
                _fingerprints.Set(
                    "marketing",
                    "active_invoices",
                    fingerprint,
                    DateTime.UtcNow,
                    rowCount: publish.RowCount,
                    published: true);
            }

            _logger.Info(
                $"live active invoices: success={publish.Success} count={publish.RowCount} profile={profile.ProfileName}@{profile.ServerName}");

            return new SnapshotJobResult(
                "marketing",
                "active_invoices",
                publish.Success,
                publish.RowCount,
                publish.Success
                    ? $"تم بث {publish.RowCount} فاتورة حية"
                    : publish.Message);
        }
        catch (Exception ex)
        {
            _logger.Error($"live active invoices failed: {ex.Message}", ex);
            return new SnapshotJobResult("marketing", "active_invoices", false, 0, ex.Message);
        }
    }

    private static string HashSummaries(IReadOnlyList<Dictionary<string, object?>> summaries)
    {
        if (summaries.Count == 0)
        {
            return "empty";
        }

        var parts = summaries
            .Select(row =>
                string.Join(
                    ",",
                    ReadString(row, "invoice_id", "invoiceId"),
                    ReadNumber(row, "line_count", "lineCount"),
                    ReadNumber(row, "total_amount", "totalAmount"),
                    ReadString(row, "last_item_at", "lastItemAt")))
            .OrderBy(value => value, StringComparer.Ordinal);
        var payload = string.Join("|", parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>
    /// Live invoice watches the Marketing DB the operator connected for sync.
    /// Prefer remote snapshot profile, then local Marketing.
    /// </summary>
    private DatabaseProfile? ResolveMarketingProfileForLive()
    {
        var all = _profileStore.GetAll().Where(profile => profile.IsEnabled).ToList();

        if (!string.IsNullOrWhiteSpace(_settings.SnapshotMarketingProfileName))
        {
            var preferred = all.FirstOrDefault(profile =>
                string.Equals(
                    profile.ProfileName,
                    _settings.SnapshotMarketingProfileName,
                    StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        var remote = all.FirstOrDefault(profile =>
            string.Equals(
                profile.ProfileName,
                ActivitySnapshotSyncService.ToRemoteProfileName("Marketing"),
                StringComparison.OrdinalIgnoreCase)
            || (string.Equals(profile.DatabaseName, "Marketing", StringComparison.OrdinalIgnoreCase)
                && !ActivitySnapshotSyncService.IsLocalServer(profile.ServerName)));
        if (remote is not null)
        {
            return remote;
        }

        return all.FirstOrDefault(profile =>
            string.Equals(profile.ProfileName, "Marketing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile.DatabaseName, "Marketing", StringComparison.OrdinalIgnoreCase));
    }

    private static string? Sanitize(string? value) =>
        ActivitySnapshotSyncService.SanitizeArabicLabel(value);

    private static string? ReadString(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && value is not null)
            {
                var text = Convert.ToString(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }

            var match = row.FirstOrDefault(pair =>
                string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Key) && match.Value is not null)
            {
                var text = Convert.ToString(match.Value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return null;
    }

    private static double? ReadNumber(Dictionary<string, object?> row, params string[] keys)
    {
        var text = ReadString(row, keys);
        if (text is null)
        {
            return null;
        }

        return double.TryParse(text, out var number) ? number : null;
    }
}
