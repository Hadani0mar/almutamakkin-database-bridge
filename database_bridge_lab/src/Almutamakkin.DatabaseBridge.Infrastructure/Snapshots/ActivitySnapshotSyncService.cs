using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

public sealed class ActivitySnapshotSyncService
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly ISqlCommandExecutor _executor;
    private readonly ISecretProtector _secretProtector;
    private readonly AppSettings _settings;
    private readonly SupabaseSnapshotIngestClient _ingestClient;
    private readonly IBridgeLogger _logger;

    public ActivitySnapshotSyncService(
        IDatabaseProfileStore profileStore,
        ISqlCommandExecutor executor,
        ISecretProtector secretProtector,
        AppSettings settings,
        SupabaseSnapshotIngestClient ingestClient,
        IBridgeLogger logger)
    {
        _profileStore = profileStore;
        _executor = executor;
        _secretProtector = secretProtector;
        _settings = settings;
        _ingestClient = ingestClient;
        _logger = logger;
    }

    public static IReadOnlyList<SnapshotSyncJobPlan> MarketingFirstWaveJobs { get; } =
    [
        new("business_profile", "بيانات النشاط", 30),
        // shortages / required_items / debts_* / expiry / purchase_orders:
        // phone pulls via bridge-relay and keeps local cache (no Supabase snapshot storage).
        // product_search removed: phone search is live via bridge-relay.
        new("debt_invoice_events", "أحداث الديون", 60),
        new("shift_close_events", "إغلاق الورديات", 90),
        new("sales_pattern", "نمط البيع", 180),
    ];

    public async Task<IReadOnlyList<SnapshotJobResult>> SyncMarketingFirstWaveAsync(
        CancellationToken cancellationToken,
        IProgress<SnapshotSyncProgress>? progress = null)
    {
        var results = new List<SnapshotJobResult>();
        progress?.Report(new SnapshotSyncProgress(
            SnapshotSyncPhase.Planned,
            Jobs: MarketingFirstWaveJobs));

        if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
            string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            results.Add(new SnapshotJobResult(
                "marketing",
                "all",
                false,
                0,
                "سجّل جهاز الجسر أولاً (tunnel + device secret)."));
            progress?.Report(new SnapshotSyncProgress(
                SnapshotSyncPhase.WaveCompleted,
                Success: false,
                Message: results[0].Message));
            return results;
        }

        var deviceSecret = _secretProtector.Unprotect(_settings.EncryptedDeviceSecret!);
        var profile = ResolveSnapshotProfile(
            preferredName: _settings.SnapshotMarketingProfileName,
            canonicalName: "Marketing");
        if (profile is null)
        {
            results.Add(new SnapshotJobResult(
                "marketing",
                "all",
                false,
                0,
                "لا يوجد ملف اتصال بعيد للمزامنة. استخدم «ربط قاعدة بعيدة» أولاً (لن يُغيَّر المحلي)."));
            progress?.Report(new SnapshotSyncProgress(
                SnapshotSyncPhase.WaveCompleted,
                Success: false,
                Message: results[0].Message));
            return results;
        }

        _logger.Info(
            $"snapshot sync using remote profile '{profile.ProfileName}' @ {profile.ServerName}/{profile.DatabaseName}");

        results.Add(await RunTrackedAsync(
            progress,
            "business_profile",
            "بيانات النشاط",
            30,
            () => RunAndPublishAsync(
                profile,
                system: "marketing",
                snapshotType: "business_profile",
                calculationVersion: "marketing_business_profile_v1",
                sqlFileName: "marketing_business_profile.sql",
                timeoutSeconds: 30,
                maxRows: 5,
                deviceSecret,
                MapBusinessProfileRow,
                cancellationToken)).ConfigureAwait(false));

        // shortages / required_items / purchase_orders / debts_* / expiry:
        // live on-demand via bridge-relay (local phone cache).
        // product_search: live on-demand via bridge-relay (not snapshotted).

        results.Add(await RunTrackedAsync(
            progress,
            "debt_invoice_events",
            "أحداث الديون",
            60,
            () => RunAndPublishAsync(
                profile,
                system: "marketing",
                snapshotType: "debt_invoice_events",
                calculationVersion: "marketing_debt_invoice_events_v1",
                sqlFileName: "marketing_debt_invoice_events.sql",
                timeoutSeconds: 60,
                maxRows: 200,
                deviceSecret,
                MapDebtInvoiceEventRow,
                cancellationToken)).ConfigureAwait(false));

        results.Add(await RunTrackedAsync(
            progress,
            "shift_close_events",
            "إغلاق الورديات",
            90,
            () => RunAndPublishAsync(
                profile,
                system: "marketing",
                snapshotType: "shift_close_events",
                calculationVersion: "marketing_shift_close_events_v1",
                sqlFileName: "marketing_shift_close_events.sql",
                timeoutSeconds: 90,
                maxRows: 100,
                deviceSecret,
                MapShiftCloseEventRow,
                cancellationToken)).ConfigureAwait(false));

        results.Add(await RunTrackedAsync(
            progress,
            "sales_pattern",
            "نمط البيع",
            180,
            () => RunAndPublishAsync(
                profile,
                system: "marketing",
                snapshotType: "sales_pattern",
                calculationVersion: "marketing_sales_pattern_v1",
                sqlFileName: "marketing_sales_pattern.sql",
                timeoutSeconds: 180,
                maxRows: 5,
                deviceSecret,
                MapSalesPatternRow,
                cancellationToken)).ConfigureAwait(false));

        var anyFailed = results.Any(result => !result.Success);
        progress?.Report(new SnapshotSyncProgress(
            SnapshotSyncPhase.WaveCompleted,
            Success: !anyFailed,
            Message: anyFailed
                ? "اكتملت الموجة مع أخطاء."
                : "اكتملت مزامنة كل اللقطات."));
        return results;
    }

    /// <summary>
    /// Publishes only debt-invoice and shift-close event snapshots for notification tests.
    /// </summary>
    public async Task<IReadOnlyList<SnapshotJobResult>> PublishNotificationEventsAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<SnapshotJobResult>();

        if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
            string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            results.Add(new SnapshotJobResult(
                "marketing",
                "all",
                false,
                0,
                "سجّل جهاز الجسر أولاً (tunnel + device secret)."));
            return results;
        }

        var deviceSecret = _secretProtector.Unprotect(_settings.EncryptedDeviceSecret!);
        var profile = ResolveSnapshotProfile(
            preferredName: _settings.SnapshotMarketingProfileName,
            canonicalName: "Marketing");
        if (profile is null)
        {
            results.Add(new SnapshotJobResult(
                "marketing",
                "all",
                false,
                0,
                "لا يوجد ملف اتصال بعيد للمزامنة. استخدم «ربط قاعدة بعيدة» أولاً (لن يُغيَّر المحلي)."));
            return results;
        }

        results.Add(await RunAndPublishAsync(
            profile,
            system: "marketing",
            snapshotType: "debt_invoice_events",
            calculationVersion: "marketing_debt_invoice_events_v1",
            sqlFileName: "marketing_debt_invoice_events.sql",
            timeoutSeconds: 60,
            maxRows: 200,
            deviceSecret,
            MapDebtInvoiceEventRow,
            cancellationToken).ConfigureAwait(false));

        results.Add(await RunAndPublishAsync(
            profile,
            system: "marketing",
            snapshotType: "shift_close_events",
            calculationVersion: "marketing_shift_close_events_v1",
            sqlFileName: "marketing_shift_close_events.sql",
            timeoutSeconds: 90,
            maxRows: 100,
            deviceSecret,
            MapShiftCloseEventRow,
            cancellationToken).ConfigureAwait(false));

        return results;
    }

    /// <summary>
    /// Publishes one Marketing snapshot type (and required_items when shortages succeed).
    /// Used by change-watch: fingerprint first, publish only on change.
    /// </summary>
    public async Task<SnapshotJobResult> PublishMarketingTypeAsync(
        string snapshotType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
            string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            return new SnapshotJobResult(
                "marketing",
                snapshotType,
                false,
                0,
                "سجّل جهاز الجسر أولاً (tunnel + device secret).");
        }

        var deviceSecret = _secretProtector.Unprotect(_settings.EncryptedDeviceSecret!);
        var profile = ResolveSnapshotProfile(
            preferredName: _settings.SnapshotMarketingProfileName,
            canonicalName: "Marketing");
        if (profile is null)
        {
            return new SnapshotJobResult(
                "marketing",
                snapshotType,
                false,
                0,
                "لا يوجد ملف اتصال بعيد للمزامنة.");
        }

        var type = snapshotType.Trim().ToLowerInvariant();
        return type switch
        {
            "business_profile" => await RunAndPublishAsync(
                profile, "marketing", "business_profile", "marketing_business_profile_v1",
                "marketing_business_profile.sql", 30, 5, deviceSecret, MapBusinessProfileRow,
                cancellationToken).ConfigureAwait(false),
            "shortages" or "required_items" or "debts_customers" or "debts_suppliers"
                or "expiry" or "purchase_orders" => new SnapshotJobResult(
                "marketing",
                snapshotType,
                true,
                0,
                "تخطي: النواقص/الصلاحية/الديون/أوامر الشراء تُسحب عبر bridge-relay إلى كاش الهاتف (بدون Supabase)."),
            "debt_invoice_events" => await RunAndPublishAsync(
                profile, "marketing", "debt_invoice_events", "marketing_debt_invoice_events_v1",
                "marketing_debt_invoice_events.sql", 60, 200, deviceSecret, MapDebtInvoiceEventRow,
                cancellationToken).ConfigureAwait(false),
            "shift_close_events" => await RunAndPublishAsync(
                profile, "marketing", "shift_close_events", "marketing_shift_close_events_v1",
                "marketing_shift_close_events.sql", 90, 100, deviceSecret, MapShiftCloseEventRow,
                cancellationToken).ConfigureAwait(false),
            "sales_pattern" => await RunAndPublishAsync(
                profile, "marketing", "sales_pattern", "marketing_sales_pattern_v1",
                "marketing_sales_pattern.sql", 180, 5, deviceSecret, MapSalesPatternRow,
                cancellationToken).ConfigureAwait(false),
            _ => new SnapshotJobResult(
                "marketing",
                snapshotType,
                false,
                0,
                $"نوع لقطة غير مدعوم للمراقبة: {snapshotType}"),
        };
    }

    /// <summary>
    /// First Infinity snapshot wave: branch profile only.
    /// Expiry/debts are pulled by the phone via bridge-relay into local Drift.
    /// Uses SnapshotInfinityProfileName / InfinityRetailDB remote profile only.
    /// </summary>
    public async Task<IReadOnlyList<SnapshotJobResult>> SyncInfinityFirstWaveAsync(
        CancellationToken cancellationToken = default,
        IProgress<SnapshotSyncProgress>? progress = null)
    {
        var results = new List<SnapshotJobResult>();

        if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
            string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            results.Add(new SnapshotJobResult(
                "infinity",
                "all",
                false,
                0,
                "سجّل جهاز الجسر أولاً (tunnel + device secret)."));
            progress?.Report(new SnapshotSyncProgress(
                SnapshotSyncPhase.WaveCompleted,
                Success: false,
                Message: results[0].Message));
            return results;
        }

        var deviceSecret = _secretProtector.Unprotect(_settings.EncryptedDeviceSecret!);
        var profile = ResolveSnapshotProfile(
            preferredName: _settings.SnapshotInfinityProfileName,
            canonicalName: "InfinityRetailDB");
        if (profile is null)
        {
            var skipped = new SnapshotJobResult(
                "infinity",
                "all",
                true,
                0,
                "تخطّي إنفينيتي: لا يوجد ملف اتصال بعيد (اربط InfinityRetailDB عند الحاجة).");
            results.Add(skipped);
            progress?.Report(new SnapshotSyncProgress(
                SnapshotSyncPhase.WaveCompleted,
                Success: true,
                Message: skipped.Message));
            return results;
        }

        _logger.Info(
            $"infinity snapshot sync using remote profile '{profile.ProfileName}' @ {profile.ServerName}/{profile.DatabaseName}");

        results.Add(await RunTrackedAsync(
            progress,
            "business_profile",
            "إنفينيتي · بيانات الفرع",
            30,
            () => RunAndPublishAsync(
                profile,
                system: "infinity",
                snapshotType: "business_profile",
                calculationVersion: "infinity_business_profile_v1",
                sqlFileName: "infinity_business_profile.sql",
                timeoutSeconds: 30,
                maxRows: 5,
                deviceSecret,
                MapInfinityBusinessProfileRow,
                cancellationToken),
            system: "infinity").ConfigureAwait(false));

        // expiry: phone pulls via bridge-relay + Drift local cache (no Supabase).

        var anyFailed = results.Any(result => !result.Success);
        progress?.Report(new SnapshotSyncProgress(
            SnapshotSyncPhase.WaveCompleted,
            Success: !anyFailed,
            Message: anyFailed
                ? "اكتملت موجة إنفينيتي مع أخطاء."
                : "اكتملت مزامنة لقطات إنفينيتي."));
        return results;
    }

    /// <summary>
    /// Runs Marketing then Infinity first waves when each remote profile exists.
    /// </summary>
    public async Task<IReadOnlyList<SnapshotJobResult>> SyncAllFirstWavesAsync(
        CancellationToken cancellationToken = default,
        IProgress<SnapshotSyncProgress>? progress = null)
    {
        var results = new List<SnapshotJobResult>();
        results.AddRange(await SyncMarketingFirstWaveAsync(cancellationToken, progress)
            .ConfigureAwait(false));
        results.AddRange(await SyncInfinityFirstWaveAsync(cancellationToken, progress)
            .ConfigureAwait(false));
        return results;
    }

    /// <summary>
    /// Publishes one Infinity snapshot type. Used by change-watch.
    /// </summary>
    public async Task<SnapshotJobResult> PublishInfinityTypeAsync(
        string snapshotType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
            string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            return new SnapshotJobResult(
                "infinity",
                snapshotType,
                false,
                0,
                "سجّل جهاز الجسر أولاً (tunnel + device secret).");
        }

        var deviceSecret = _secretProtector.Unprotect(_settings.EncryptedDeviceSecret!);
        var profile = ResolveSnapshotProfile(
            preferredName: _settings.SnapshotInfinityProfileName,
            canonicalName: "InfinityRetailDB");
        if (profile is null)
        {
            return new SnapshotJobResult(
                "infinity",
                snapshotType,
                false,
                0,
                "لا يوجد ملف اتصال بعيد لإنفينيتي.");
        }

        var type = snapshotType.Trim().ToLowerInvariant();
        return type switch
        {
            "business_profile" => await RunAndPublishAsync(
                profile, "infinity", "business_profile", "infinity_business_profile_v1",
                "infinity_business_profile.sql", 30, 5, deviceSecret, MapInfinityBusinessProfileRow,
                cancellationToken).ConfigureAwait(false),
            "expiry" => await RunAndPublishAsync(
                profile, "infinity", "expiry", "infinity_expiry_v1",
                "infinity_expiry.sql", 120, 5000, deviceSecret, MapInfinityExpiryRow,
                cancellationToken).ConfigureAwait(false),
            _ => new SnapshotJobResult(
                "infinity",
                snapshotType,
                false,
                0,
                $"نوع لقطة إنفينيتي غير مدعوم للمراقبة: {snapshotType}"),
        };
    }

    private async Task<SnapshotJobResult> PublishShortagesAndRequiredAsync(
        DatabaseProfile profile,
        string deviceSecret,
        CancellationToken cancellationToken)
    {
        var shortagesWave = await RunAndPublishWithMappedRowsAsync(
            profile,
            system: "marketing",
            snapshotType: "shortages",
            calculationVersion: "marketing_shortages_v1",
            sqlFileName: "marketing_shortages.sql",
            timeoutSeconds: 120,
            maxRows: 5000,
            deviceSecret,
            MapShortageRow,
            cancellationToken).ConfigureAwait(false);

        if (!shortagesWave.Result.Success)
        {
            return shortagesWave.Result;
        }

        var required = await PublishDerivedRequiredItemsAsync(
            deviceSecret,
            shortagesWave.MappedRows,
            cancellationToken).ConfigureAwait(false);

        return required.Success
            ? new SnapshotJobResult(
                "marketing",
                "required_items",
                true,
                required.RowCount,
                $"نواقص + مطلوب: {shortagesWave.Result.RowCount}/{required.RowCount}")
            : required;
    }

    private static async Task<SnapshotJobResult> RunTrackedAsync(
        IProgress<SnapshotSyncProgress>? progress,
        string snapshotType,
        string displayName,
        int estimatedSeconds,
        Func<Task<SnapshotJobResult>> run,
        string system = "marketing")
    {
        progress?.Report(new SnapshotSyncProgress(
            SnapshotSyncPhase.Started,
            SnapshotType: snapshotType,
            DisplayName: displayName,
            EstimatedSeconds: estimatedSeconds));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        SnapshotJobResult result;
        try
        {
            result = await run().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new SnapshotJobResult(system, snapshotType, false, 0, ex.Message);
        }

        sw.Stop();
        progress?.Report(new SnapshotSyncProgress(
            SnapshotSyncPhase.Completed,
            SnapshotType: snapshotType,
            DisplayName: displayName,
            EstimatedSeconds: estimatedSeconds,
            Success: result.Success,
            RowCount: result.RowCount,
            Message: result.Message,
            Elapsed: sw.Elapsed));
        return result;
    }

    private static async Task<(SnapshotJobResult Result, List<Dictionary<string, object?>> MappedRows)>
        RunTrackedWithMappedRowsAsync(
            IProgress<SnapshotSyncProgress>? progress,
            string snapshotType,
            string displayName,
            int estimatedSeconds,
            Func<Task<(SnapshotJobResult Result, List<Dictionary<string, object?>> MappedRows)>> run)
    {
        progress?.Report(new SnapshotSyncProgress(
            SnapshotSyncPhase.Started,
            SnapshotType: snapshotType,
            DisplayName: displayName,
            EstimatedSeconds: estimatedSeconds));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        (SnapshotJobResult Result, List<Dictionary<string, object?>> MappedRows) wave;
        try
        {
            wave = await run().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            wave = (
                new SnapshotJobResult("marketing", snapshotType, false, 0, ex.Message),
                new List<Dictionary<string, object?>>());
        }

        sw.Stop();
        progress?.Report(new SnapshotSyncProgress(
            SnapshotSyncPhase.Completed,
            SnapshotType: snapshotType,
            DisplayName: displayName,
            EstimatedSeconds: estimatedSeconds,
            Success: wave.Result.Success,
            RowCount: wave.Result.RowCount,
            Message: wave.Result.Message,
            Elapsed: sw.Elapsed));
        return wave;
    }

    private async Task<SnapshotJobResult> RunAndPublishPurchaseOrdersAsync(
        DatabaseProfile profile,
        string system,
        string calculationVersion,
        string sqlFileName,
        int timeoutSeconds,
        int maxRows,
        string deviceSecret,
        CancellationToken cancellationToken)
    {
        const string snapshotType = "purchase_orders";
        try
        {
            var sql = SnapshotSqlFiles.Read(sqlFileName);
            var execution = await _executor.ExecuteAsync(
                profile,
                new SqlExecutePayload
                {
                    DatabaseProfile = profile.ProfileName,
                    Sql = sql,
                    TimeoutSeconds = timeoutSeconds,
                    MaxRows = maxRows,
                },
                cancellationToken).ConfigureAwait(false);

            if (!execution.Success)
            {
                return new SnapshotJobResult(
                    system,
                    snapshotType,
                    false,
                    0,
                    execution.ErrorMessage ?? "فشل تنفيذ استعلام أمر الشراء.");
            }

            var sourceRows = execution.ResultSets.FirstOrDefault()?.Rows
                ?? new List<Dictionary<string, object?>>();
            var headers = BuildPurchaseOrderHeadersBySupplier(sourceRows);

            var publish = await _ingestClient.PublishAsync(
                _settings,
                deviceSecret,
                new SnapshotIngestRequest
                {
                    TunnelId = _settings.TunnelId,
                    System = system,
                    SnapshotType = snapshotType,
                    CalculationVersion = calculationVersion,
                    Params = new Dictionary<string, object?>
                    {
                        ["sourceSql"] = sqlFileName,
                        ["executionTimeMs"] = execution.ExecutionTimeMs,
                        ["wasTruncated"] = execution.WasTruncated,
                        ["supplierCount"] = headers.Count,
                        ["grouping"] = "by_supplier",
                    },
                    GeneratedAt = DateTime.UtcNow.ToString("O"),
                    Rows = new List<Dictionary<string, object?>>(),
                    Headers = headers,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.Info(
                $"snapshot {system}/{snapshotType}: success={publish.Success} rows={publish.RowCount} suppliers={headers.Count}");

            return new SnapshotJobResult(
                system,
                snapshotType,
                publish.Success,
                publish.RowCount,
                publish.Success
                    ? $"تم رفع {headers.Count} مورداً / {publish.RowCount} صنفاً ({publish.SnapshotId})"
                    : publish.Message);
        }
        catch (Exception ex)
        {
            _logger.Error($"snapshot {system}/{snapshotType} failed: {ex.Message}", ex);
            return new SnapshotJobResult(system, snapshotType, false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Groups smart-purchase line items by supplier exactly like the Flutter app:
    /// one header per supplier, items sorted by priority then name, headers by supplier name.
    /// Header totals count only Confirmed/Recommended (approved for export).
    /// </summary>
    private static List<Dictionary<string, object?>> BuildPurchaseOrderHeadersBySupplier(
        IReadOnlyList<Dictionary<string, object?>> sourceRows)
    {
        var grouped = new Dictionary<string, List<Dictionary<string, object?>>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in sourceRows)
        {
            var supplierId = ReadString(row, "supplier_id", "supplierId") ?? "0";
            if (!grouped.TryGetValue(supplierId, out var list))
            {
                list = new List<Dictionary<string, object?>>();
                grouped[supplierId] = list;
            }

            list.Add(row);
        }

        var headers = new List<(string Name, Dictionary<string, object?> Header)>();
        foreach (var (supplierId, rows) in grouped)
        {
            rows.Sort((a, b) =>
            {
                var scoreCompare = (ReadNumber(b, "priority_score", "priorityScore") ?? 0)
                    .CompareTo(ReadNumber(a, "priority_score", "priorityScore") ?? 0);
                if (scoreCompare != 0)
                {
                    return scoreCompare;
                }

                return string.Compare(
                    ReadString(a, "item_name", "itemName") ?? string.Empty,
                    ReadString(b, "item_name", "itemName") ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            });

            var first = rows[0];
            var supplierName = SanitizeArabicLabel(
                ReadString(first, "supplier_name", "supplierName"),
                fallback: "غير محدد")!;
            var mobile = ReadString(first, "supplier_mobile", "supplierMobile");
            var phone = ReadString(first, "supplier_phone", "supplierPhone");
            var preferredPhone = !string.IsNullOrWhiteSpace(mobile) ? mobile : phone;

            var items = new List<Dictionary<string, object?>>(rows.Count);
            var approvedCount = 0;
            double approvedCost = 0;

            foreach (var row in rows)
            {
                var decision = ReadString(row, "decision", "decisionCode") ?? string.Empty;
                var isApproved = decision is "Confirmed" or "Recommended";
                var estimatedCost = ReadNumber(row, "estimated_cost", "estimatedCost") ?? 0;
                if (isApproved)
                {
                    approvedCount++;
                    approvedCost += estimatedCost;
                }

                items.Add(new Dictionary<string, object?>
                {
                    ["itemId"] = ReadString(row, "item_id", "itemId"),
                    ["itemName"] = SanitizeArabicLabel(
                        ReadString(row, "item_name", "itemName"),
                        fallback: "صنف")!,
                    ["purchaseUnitLabel"] = SanitizeArabicLabel(
                        ReadString(row, "purchase_unit", "purchaseUnit", "purchaseUnitLabel")),
                    ["suggestedQty"] = ReadNumber(row, "suggested_qty", "suggestedQty"),
                    ["suggestedPacks"] = ReadNumber(row, "suggested_packs", "suggestedPacks"),
                    ["unitCost"] = ReadNumber(row, "unit_cost", "unitCost"),
                    ["estimatedCost"] = estimatedCost,
                    ["currentStock"] = ReadNumber(row, "sellable_stock", "sellableStock", "currentStock"),
                    ["coverageDays"] = ReadNumber(row, "coverage_days", "coverageDays"),
                    ["decisionCode"] = decision,
                    ["extras"] = new Dictionary<string, object?>
                    {
                        ["packSize"] = ReadNumber(row, "pack_size", "packSize"),
                        ["priorityScore"] = ReadNumber(row, "priority_score", "priorityScore"),
                        ["forecastDaily"] = ReadNumber(row, "forecast_daily", "forecastDaily"),
                        ["marginPct"] = ReadNumber(row, "margin_pct", "marginPct"),
                        ["sales30"] = ReadNumber(row, "sales_30", "sales30"),
                        ["demandPattern"] = ReadString(row, "demand_pattern", "demandPattern"),
                        ["confidence"] = ReadString(row, "confidence", "confidenceCode"),
                        ["supplierCode"] = ReadString(row, "supplier_code", "supplierCode"),
                        ["isApprovedForExport"] = isApproved,
                    },
                });
            }

            headers.Add((
                supplierName,
                new Dictionary<string, object?>
                {
                    ["supplierId"] = supplierId,
                    ["supplierName"] = supplierName,
                    ["supplierPhone"] = preferredPhone,
                    ["itemCount"] = approvedCount,
                    ["totalEstimatedCost"] = approvedCost,
                    ["items"] = items,
                    ["extras"] = new Dictionary<string, object?>
                    {
                        ["supplierCode"] = ReadString(first, "supplier_code", "supplierCode"),
                        ["supplierMobile"] = mobile,
                        ["supplierAddress"] = ReadString(first, "supplier_address", "supplierAddress"),
                        ["supplierEmail"] = ReadString(first, "supplier_email", "supplierEmail"),
                        ["allItemCount"] = rows.Count,
                        ["approvedItemCount"] = approvedCount,
                    },
                }));
        }

        headers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return headers.Select(pair => pair.Header).ToList();
    }

    private async Task<SnapshotJobResult> RunAndPublishAsync(
        DatabaseProfile profile,
        string system,
        string snapshotType,
        string calculationVersion,
        string sqlFileName,
        int timeoutSeconds,
        int maxRows,
        string deviceSecret,
        Func<Dictionary<string, object?>, int, Dictionary<string, object?>> mapRow,
        CancellationToken cancellationToken)
    {
        var bundle = await RunAndPublishWithMappedRowsAsync(
            profile,
            system,
            snapshotType,
            calculationVersion,
            sqlFileName,
            timeoutSeconds,
            maxRows,
            deviceSecret,
            mapRow,
            cancellationToken).ConfigureAwait(false);
        return bundle.Result;
    }

    private async Task<(SnapshotJobResult Result, List<Dictionary<string, object?>> MappedRows)>
        RunAndPublishWithMappedRowsAsync(
            DatabaseProfile profile,
            string system,
            string snapshotType,
            string calculationVersion,
            string sqlFileName,
            int timeoutSeconds,
            int maxRows,
            string deviceSecret,
            Func<Dictionary<string, object?>, int, Dictionary<string, object?>> mapRow,
            CancellationToken cancellationToken)
    {
        var empty = new List<Dictionary<string, object?>>();
        try
        {
            var sql = SnapshotSqlFiles.Read(sqlFileName);
            var execution = await _executor.ExecuteAsync(
                profile,
                new SqlExecutePayload
                {
                    DatabaseProfile = profile.ProfileName,
                    Sql = sql,
                    TimeoutSeconds = timeoutSeconds,
                    MaxRows = maxRows,
                },
                cancellationToken).ConfigureAwait(false);

            if (!execution.Success)
            {
                return (
                    new SnapshotJobResult(
                        system,
                        snapshotType,
                        false,
                        0,
                        execution.ErrorMessage ?? "فشل تنفيذ الاستعلام."),
                    empty);
            }

            var sourceRows = execution.ResultSets.FirstOrDefault()?.Rows
                ?? new List<Dictionary<string, object?>>();
            var mapped = new List<Dictionary<string, object?>>(sourceRows.Count);
            for (var i = 0; i < sourceRows.Count; i++)
            {
                mapped.Add(mapRow(sourceRows[i], i));
            }

            var publish = await _ingestClient.PublishAsync(
                _settings,
                deviceSecret,
                new SnapshotIngestRequest
                {
                    TunnelId = _settings.TunnelId,
                    System = system,
                    SnapshotType = snapshotType,
                    CalculationVersion = calculationVersion,
                    Params = BuildSnapshotParams(
                        snapshotType,
                        sqlFileName,
                        execution.ExecutionTimeMs,
                        execution.WasTruncated),
                    GeneratedAt = DateTime.UtcNow.ToString("O"),
                    Rows = mapped,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.Info(
                $"snapshot {system}/{snapshotType}: success={publish.Success} rows={publish.RowCount}");

            return (
                new SnapshotJobResult(
                    system,
                    snapshotType,
                    publish.Success,
                    publish.RowCount,
                    publish.Success
                        ? $"تم رفع {publish.RowCount} صفاً ({publish.SnapshotId})"
                        : publish.Message),
                mapped);
        }
        catch (Exception ex)
        {
            _logger.Error($"snapshot {system}/{snapshotType} failed: {ex.Message}", ex);
            return (new SnapshotJobResult(system, snapshotType, false, 0, ex.Message), empty);
        }
    }

    /// <summary>
    /// Builds Marketing required_items rows from shortage snapshot rows using the
    /// Infinity-compatible filter: net_required &gt;= 1 and days_of_cover &lt; 22.
    /// </summary>
    private async Task<SnapshotJobResult> PublishDerivedRequiredItemsAsync(
        string deviceSecret,
        IReadOnlyList<Dictionary<string, object?>> shortageRows,
        CancellationToken cancellationToken)
    {
        const string system = "marketing";
        const string snapshotType = "required_items";
        const string calculationVersion = "marketing_required_items_from_shortages_v1";

        try
        {
            var derived = DeriveRequiredItemsFromShortages(shortageRows);
            var publish = await _ingestClient.PublishAsync(
                _settings,
                deviceSecret,
                new SnapshotIngestRequest
                {
                    TunnelId = _settings.TunnelId,
                    System = system,
                    SnapshotType = snapshotType,
                    CalculationVersion = calculationVersion,
                    Params = new Dictionary<string, object?>
                    {
                        ["derivedFrom"] = "shortages",
                        ["sourceCalculation"] = "marketing_shortages_v1",
                        ["filter"] = "net_required>=1 AND days_of_cover<22",
                        ["coverageTargetDays"] = 35,
                        ["demandWindowDays"] = 30,
                        ["note"] =
                            "Nucleus only: Marketing shortages math (30d+7d accel), not full Infinity 90d availability ledger.",
                    },
                    GeneratedAt = DateTime.UtcNow.ToString("O"),
                    Rows = derived,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.Info(
                $"snapshot {system}/{snapshotType}: success={publish.Success} rows={publish.RowCount}");

            return new SnapshotJobResult(
                system,
                snapshotType,
                publish.Success,
                publish.RowCount,
                publish.Success
                    ? $"مشتق من النواقص: {publish.RowCount} صنفاً ({publish.SnapshotId})"
                    : publish.Message);
        }
        catch (Exception ex)
        {
            _logger.Error($"snapshot {system}/{snapshotType} failed: {ex.Message}", ex);
            return new SnapshotJobResult(system, snapshotType, false, 0, ex.Message);
        }
    }

    public static List<Dictionary<string, object?>> DeriveRequiredItemsFromShortages(
        IReadOnlyList<Dictionary<string, object?>> shortageRows)
    {
        var derived = new List<Dictionary<string, object?>>();
        var sort = 0;
        foreach (var row in shortageRows)
        {
            var netRequired = AsDouble(row.GetValueOrDefault("suggestedOrderQty"))
                ?? AsDouble(ReadNestedExtra(row, "suggestedOrderQty", "REQUIRED_QTY"));
            var daysOfCover = AsDouble(row.GetValueOrDefault("daysOfCover"))
                ?? AsDouble(ReadNestedExtra(row, "daysOfStockCover", "DAYS_COVER"));
            if (netRequired is null || netRequired < 1)
            {
                continue;
            }

            if (daysOfCover is null || daysOfCover >= 22)
            {
                continue;
            }

            var itemId = ReadString(row, "itemId")
                ?? ReadNestedExtraString(row, "itemId", "ITEM_ID");
            var itemName = ReadString(row, "itemName")
                ?? ReadNestedExtraString(row, "itemName", "ITEM_NAME")
                ?? "صنف";
            var supplier = SanitizeArabicLabel(
                ReadString(row, "supplierName")
                ?? ReadNestedExtraString(row, "supplierName", "SUPPLIER_NAME"),
                fallback: "غير محدد");
            var stock = AsDouble(row.GetValueOrDefault("currentStock"))
                ?? AsDouble(ReadNestedExtra(row, "currentStock", "CURRENT_STOCK"))
                ?? 0;
            var avgDaily = AsDouble(ReadNestedExtra(
                    row,
                    "forecastDailySales",
                    "FORECAST_DAILY",
                    "avgDaily"))
                ?? AsDouble(ReadNestedExtra(row, "averageDailySales30", "AVG_DAILY_30"));
            var price = AsDouble(row.GetValueOrDefault("lastPurchasePrice"))
                ?? AsDouble(ReadNestedExtra(row, "lastPurchasePrice", "LAST_PURCHASE_PRICE"))
                ?? 0;
            var itemCode = ReadNestedExtraString(row, "itemCode", "ITEM_CODE", "ITEM_MODEL");
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                itemCode = itemId;
            }

            derived.Add(new Dictionary<string, object?>
            {
                ["sortOrder"] = sort++,
                ["itemId"] = itemId,
                ["itemCode"] = itemCode,
                ["itemName"] = itemName,
                ["netRequired"] = netRequired,
                ["stockQty"] = stock,
                ["avgDaily"] = avgDaily,
                ["daysOfCover"] = daysOfCover,
                ["mainSupplierName"] = supplier,
                // Marketing shortages expose one supplier (last purchase); mirror until cheap-supplier is added.
                ["flexibleSupplierName"] = supplier,
                ["estimatedValue"] = Math.Round(netRequired.Value * price, 2),
                ["extras"] = new Dictionary<string, object?>
                {
                    ["algorithmCore"] = new Dictionary<string, object?>
                    {
                        ["coverageTargetDays"] = 35,
                        ["demandWindowDays"] = 30,
                        ["accelerationWindowDays"] = 7,
                        ["requiredFilterMaxCoverDays"] = 22,
                        ["lastPurchasePrice"] = price,
                        ["netSales30"] = AsDouble(row.GetValueOrDefault("netSales30"))
                            ?? AsDouble(ReadNestedExtra(row, "netSales30Days", "NET_SOLD_30")),
                        ["targetStock35"] = AsDouble(ReadNestedExtra(
                            row,
                            "targetStock35Days",
                            "TARGET_STOCK")),
                        ["derivedFrom"] = "shortages",
                    },
                },
            });
        }

        derived.Sort((a, b) =>
        {
            var coverCompare = (AsDouble(a.GetValueOrDefault("daysOfCover")) ?? 999)
                .CompareTo(AsDouble(b.GetValueOrDefault("daysOfCover")) ?? 999);
            if (coverCompare != 0)
            {
                return coverCompare;
            }

            return (AsDouble(b.GetValueOrDefault("netRequired")) ?? 0)
                .CompareTo(AsDouble(a.GetValueOrDefault("netRequired")) ?? 0);
        });

        for (var i = 0; i < derived.Count; i++)
        {
            derived[i]["sortOrder"] = i;
        }

        return derived;
    }

    private static object? ReadNestedExtra(
        Dictionary<string, object?> row,
        params string[] keys)
    {
        if (row.TryGetValue("extras", out var extrasObj) &&
            extrasObj is Dictionary<string, object?> extras)
        {
            foreach (var key in keys)
            {
                if (extras.TryGetValue(key, out var value) && value is not null)
                {
                    return value;
                }

                var match = extras.FirstOrDefault(pair =>
                    string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match.Key) && match.Value is not null)
                {
                    return match.Value;
                }
            }
        }

        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? ReadNestedExtraString(
        Dictionary<string, object?> row,
        params string[] keys)
    {
        var value = ReadNestedExtra(row, keys);
        var text = Convert.ToString(value)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static double? AsDouble(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is double d)
        {
            return d;
        }

        if (value is float f)
        {
            return f;
        }

        if (value is decimal m)
        {
            return (double)m;
        }

        if (value is int i)
        {
            return i;
        }

        if (value is long l)
        {
            return l;
        }

        return double.TryParse(
            Convert.ToString(value),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private DatabaseProfile? ResolveSnapshotProfile(string? preferredName, string canonicalName)
    {
        var all = _profileStore.GetAll().Where(profile => profile.IsEnabled).ToList();

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var preferred = all.FirstOrDefault(profile =>
                string.Equals(profile.ProfileName, preferredName, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null && !IsLocalServer(preferred.ServerName))
            {
                return preferred;
            }
        }

        var remoteNamed = all.FirstOrDefault(profile =>
            string.Equals(
                profile.ProfileName,
                ToRemoteProfileName(canonicalName),
                StringComparison.OrdinalIgnoreCase) &&
            !IsLocalServer(profile.ServerName));
        if (remoteNamed is not null)
        {
            return remoteNamed;
        }

        // Any enabled remote profile whose database matches the canonical DB name.
        return all.FirstOrDefault(profile =>
            !IsLocalServer(profile.ServerName) &&
            string.Equals(profile.DatabaseName, canonicalName, StringComparison.OrdinalIgnoreCase));
    }

    public static string ToRemoteProfileName(string canonicalName) =>
        $"{canonicalName}__remote";

    public static bool IsLocalServer(string? serverName)
    {
        var value = (serverName ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value is "localhost" or "." or "(local)" or "127.0.0.1" or "::1"
            || value.StartsWith("localhost,", StringComparison.Ordinal)
            || value.StartsWith("127.0.0.1,", StringComparison.Ordinal)
            || value.StartsWith(".\\", StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> BuildSnapshotParams(
        string snapshotType,
        string sqlFileName,
        long executionTimeMs,
        bool wasTruncated)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["sourceSql"] = sqlFileName,
            ["executionTimeMs"] = executionTimeMs,
            ["wasTruncated"] = wasTruncated,
        };

        if (string.Equals(snapshotType, "daily_statistics", StringComparison.Ordinal))
        {
            var today = DateTime.Now.Date;
            parameters["periodStart"] = today.ToString("yyyy-MM-dd");
            parameters["periodEnd"] = today.ToString("yyyy-MM-dd");
        }

        return parameters;
    }

    private static Dictionary<string, object?> MapDailyStatisticsRow(
        Dictionary<string, object?> row,
        int index)
    {
        return new Dictionary<string, object?>
        {
            ["sortOrder"] = index,
            ["employeeId"] = ReadString(row, "employee_id", "employeeId"),
            ["employeeName"] = SanitizeArabicLabel(
                ReadString(row, "employee_name", "employeeName"),
                fallback: "غير محدد"),
            ["totalSales"] = ReadNumber(row, "total_sales", "totalSales"),
            ["cashSales"] = ReadNumber(row, "cash_sales", "cashSales"),
            ["debtSales"] = ReadNumber(row, "debt_sales", "debtSales"),
            ["totalPurchases"] = ReadNumber(row, "total_purchases", "totalPurchases"),
            ["cashDocumentCount"] = ReadNumber(row, "cash_document_count", "cashDocumentCount"),
            ["debtInvoiceCount"] = ReadNumber(row, "debt_invoice_count", "debtInvoiceCount"),
            ["cashSaleLineCount"] = ReadNumber(row, "cash_sale_line_count", "cashSaleLineCount"),
            ["openCashInvoiceCount"] = ReadNumber(row, "open_cash_invoice_count", "openCashInvoiceCount"),
            ["openDebtInvoiceCount"] = ReadNumber(row, "open_debt_invoice_count", "openDebtInvoiceCount"),
            ["openInvoiceSales"] = ReadNumber(row, "open_invoice_sales", "openInvoiceSales"),
            ["purchaseInvoices"] = ReadNumber(row, "purchase_invoices", "purchaseInvoices"),
            ["soldQty"] = ReadNumber(row, "sold_qty", "soldQty"),
            ["purchasedQty"] = ReadNumber(row, "purchased_qty", "purchasedQty"),
            ["closedShiftCount"] = ReadNumber(row, "closed_shift_count", "closedShiftCount"),
            ["extras"] = new Dictionary<string, object?>(),
        };
    }

    private static Dictionary<string, object?> MapDebtInvoiceEventRow(
        Dictionary<string, object?> row,
        int index) =>
        new()
        {
            ["sortOrder"] = index,
            ["saleId"] = ReadString(row, "sale_id", "saleId"),
            ["saleDate"] = ReadString(row, "sale_date", "saleDate"),
            ["customerName"] = SanitizeArabicLabel(
                ReadString(row, "customer_name", "customerName"),
                fallback: "عميل"),
            ["employeeName"] = SanitizeArabicLabel(
                ReadString(row, "employee_name", "employeeName"),
                fallback: "مجهول"),
            ["totalAmount"] = ReadNumber(row, "total_amount", "totalAmount"),
            ["extras"] = new Dictionary<string, object?>(),
        };

    private static Dictionary<string, object?> MapShiftCloseEventRow(
        Dictionary<string, object?> row,
        int index) =>
        new()
        {
            ["sortOrder"] = index,
            ["shiftId"] = ReadString(row, "shift_id", "shiftId"),
            ["employeeNo"] = ReadString(row, "employee_no", "employeeNo", "EMPLOYEE_NO"),
            ["employeeName"] = SanitizeArabicLabel(
                ReadString(row, "employee_name", "employeeName"),
                fallback: "مجهول"),
            ["checkIn"] = ReadString(row, "check_in", "checkIn"),
            ["checkOut"] = ReadString(row, "check_out", "checkOut"),
            ["hours"] = ReadNumber(row, "hours", "hours_value"),
            ["sessionKey"] = ReadString(row, "session_key", "sessionKey", "SESSION_KEY"),
            ["shiftMinutes"] = ReadNumber(row, "shift_minutes", "shiftMinutes"),
            ["cashRevenue"] = ReadNumber(row, "cash_revenue", "cashRevenue"),
            ["debtRevenue"] = ReadNumber(row, "debt_revenue", "debtRevenue"),
            ["totalRevenue"] = ReadNumber(row, "total_revenue", "totalRevenue"),
            ["extras"] = new Dictionary<string, object?>(),
        };

    private static Dictionary<string, object?> MapSalesPatternRow(
        Dictionary<string, object?> row,
        int index) =>
        new()
        {
            ["sortOrder"] = index,
            ["cashInvoiceCount"] = ReadNumber(row, "cash_invoice_count", "cashInvoiceCount"),
            ["longCashInvoicePercent"] = ReadNumber(
                row,
                "long_cash_invoice_percent",
                "longCashInvoicePercent"),
            ["largeCashInvoicePercent"] = ReadNumber(
                row,
                "large_cash_invoice_percent",
                "largeCashInvoicePercent"),
            ["shortCashInvoicePercent"] = ReadNumber(
                row,
                "short_cash_invoice_percent",
                "shortCashInvoicePercent"),
            ["averageCashDurationMinutes"] = ReadNumber(
                row,
                "average_cash_duration_minutes",
                "averageCashDurationMinutes"),
            ["averageCashLineCount"] = ReadNumber(
                row,
                "average_cash_line_count",
                "averageCashLineCount"),
            ["saleLineCount"] = ReadNumber(row, "sale_line_count", "saleLineCount"),
            ["smallSaleLines"] = ReadNumber(row, "small_sale_lines", "smallSaleLines"),
            ["bulkSaleLines"] = ReadNumber(row, "bulk_sale_lines", "bulkSaleLines"),
            ["packagedSaleLines"] = ReadNumber(row, "packaged_sale_lines", "packagedSaleLines"),
            ["averageBaseQuantity"] = ReadNumber(
                row,
                "average_base_quantity",
                "averageBaseQuantity"),
            ["extras"] = new Dictionary<string, object?>(),
        };

    private static Dictionary<string, object?> MapProductSearchRow(
        Dictionary<string, object?> row,
        int index)
    {
        var itemId = ReadString(row, "item_id", "itemId", "ITEM_ID");
        var barcode = ReadString(row, "barcode", "BARCODE");
        var unitLabel = SanitizeArabicLabel(
            ReadString(row, "unit_label", "unitLabel"),
            fallback: "وحدة أساسية");
        var baseUnit = SanitizeArabicLabel(
            ReadString(row, "base_unit_label", "baseUnitLabel"),
            fallback: "وحدة أساسية");
        var supplier = SanitizeArabicLabel(
            ReadString(row, "last_supplier_name", "lastSupplierName"),
            fallback: "لا يوجد شراء سابق");

        return new Dictionary<string, object?>
        {
            ["sortOrder"] = index,
            ["itemId"] = itemId,
            ["itemName"] = SanitizeArabicLabel(
                ReadString(row, "item_name", "itemName", "ITEM_NAME"),
                fallback: "صنف"),
            ["barcode"] = barcode,
            ["barId"] = ReadString(row, "bar_id", "barId", "BAR_ID"),
            ["salePrice"] = ReadNumber(row, "sale_price", "salePrice", "PRICE1"),
            ["unitLabel"] = unitLabel,
            ["unitFactor"] = ReadNumber(row, "unit_factor", "unitFactor"),
            ["stockQty"] = ReadNumber(row, "stock_qty", "stockQty", "CURRENT_QTY"),
            ["baseUnitLabel"] = baseUnit,
            ["nearestExpiry"] = ReadString(row, "nearest_expiry", "nearestExpiry"),
            ["nearestExpiryQty"] = ReadNumber(row, "nearest_expiry_qty", "nearestExpiryQty"),
            ["lastBuyPrice"] = ReadNumber(row, "last_buy_price", "lastBuyPrice"),
            ["lastSupplierName"] = supplier,
            ["lastBuyDate"] = ReadString(row, "last_buy_date", "lastBuyDate"),
            ["extras"] = new Dictionary<string, object?>
            {
                ["ui"] = new Dictionary<string, object?>
                {
                    ["suggestionFields"] = new[]
                    {
                        "itemName",
                        "barcode",
                        "salePrice",
                        "unitLabel",
                    },
                    ["detailFields"] = new[]
                    {
                        "itemId",
                        "barcode",
                        "stockQty",
                        "baseUnitLabel",
                        "unitLabel",
                        "salePrice",
                        "nearestExpiry",
                        "lastBuyPrice",
                        "lastSupplierName",
                        "lastBuyDate",
                    },
                    ["sourceScreen"] = "home_screen",
                },
                ["searchKeys"] = new Dictionary<string, object?>
                {
                    ["itemId"] = itemId,
                    ["barcode"] = barcode,
                    ["itemName"] = ReadString(row, "item_name", "itemName"),
                },
            },
        };
    }

    private static Dictionary<string, object?> MapBusinessProfileRow(
        Dictionary<string, object?> row,
        int index)
    {
        var businessName = SanitizeArabicLabel(
            ReadString(row, "business_name", "businessName", "A_NAME"));
        var activityName = SanitizeArabicLabel(
            ReadString(row, "activity_name", "activityName", "ACTIVITYName"));
        return new Dictionary<string, object?>
        {
            ["sortOrder"] = index,
            ["businessName"] = businessName,
            ["activityName"] = activityName,
            ["address"] = SanitizeArabicLabel(
                ReadString(row, "address", "A_ADDRESS")),
            ["city"] = SanitizeArabicLabel(ReadString(row, "city", "CITY")),
            ["phone"] = ReadString(row, "phone", "PHONE"),
            ["extras"] = new Dictionary<string, object?>
            {
                ["displayName"] = !string.IsNullOrWhiteSpace(businessName)
                    ? businessName
                    : activityName,
            },
        };
    }

    private static Dictionary<string, object?> MapInfinityBusinessProfileRow(
        Dictionary<string, object?> row,
        int index)
    {
        var branchName = SanitizeArabicLabel(
            ReadString(row, "business_name", "businessName", "BranchName"),
            fallback: "فرع غير محدد")!;
        return new Dictionary<string, object?>
        {
            ["sortOrder"] = index,
            ["branchId"] = ReadNumber(row, "branch_id", "branchId", "BranchID_PK"),
            ["branchName"] = branchName,
            ["address"] = SanitizeArabicLabel(ReadString(row, "address")),
            ["phone"] = ReadString(row, "phone", "BranchPhone"),
            ["email"] = ReadString(row, "email", "BranchEmailAddress"),
            ["extras"] = new Dictionary<string, object?>
            {
                ["system"] = "infinity",
            },
        };
    }

    private static Dictionary<string, object?> MapShortageRow(
        Dictionary<string, object?> row,
        int index)
    {
        var statusCode = ReadString(row, "shortageStatusCode", "SHORTAGE_STATUS_CODE");
        // Color key for UI dots (app renders later). Do not store Arabic labels here.
        // red = نفد/حرج, yellow = منخفض/يحتاج طلب, green = جيد
        var statusColor = ResolveShortageDotColor(statusCode);
        var itemId = ReadString(row, "itemId", "ITEM_ID");
        var netRequired = ReadNumber(row, "suggestedOrderQty", "REQUIRED_QTY");
        var stock = ReadNumber(row, "currentStock", "CURRENT_STOCK");
        var daysOfCover = ReadNumber(row, "daysOfStockCover", "DAYS_COVER");
        var avgDaily = ReadNumber(row, "forecastDailySales", "FORECAST_DAILY");
        var price = ReadNumber(row, "lastPurchasePrice", "LAST_PURCHASE_PRICE");
        var targetStock = ReadNumber(row, "targetStock35Days", "TARGET_STOCK");
        double? estimatedValue = null;
        if (netRequired is not null && price is not null)
        {
            estimatedValue = Math.Round(netRequired.Value * price.Value, 2);
        }
        var itemCode = ReadString(row, "itemCode", "ITEM_CODE", "ITEM_MODEL");
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            itemCode = itemId;
        }

        // Keep raw SQL aliases for compatibility, and stamp algorithmCore for required_items derivation.
        var extras = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)
        {
            ["itemCode"] = itemCode,
            ["avgDaily"] = avgDaily,
            ["estimatedValue"] = estimatedValue,
            ["algorithmCore"] = new Dictionary<string, object?>
            {
                ["coverageTargetDays"] = 35,
                ["demandWindowDays"] = 30,
                ["accelerationWindowDays"] = 7,
                ["requiredFilterMaxCoverDays"] = 22,
                ["avgDaily"] = avgDaily,
                ["avgDaily30"] = ReadNumber(row, "averageDailySales30", "AVG_DAILY_30"),
                ["avgDaily7"] = ReadNumber(row, "averageDailySales7", "AVG_DAILY_7"),
                ["targetStock35"] = targetStock,
                ["netRequired"] = netRequired,
                ["stockQty"] = stock,
                ["daysOfCover"] = daysOfCover,
                ["lastPurchasePrice"] = price,
                ["estimatedValue"] = estimatedValue,
            },
        };

        return new Dictionary<string, object?>
        {
            ["sortOrder"] = index,
            ["itemId"] = itemId,
            ["itemName"] = ReadString(row, "itemName", "ITEM_NAME"),
            ["categoryName"] = ReadString(row, "categoryName", "CAT1_NAME"),
            ["supplierName"] = SanitizeArabicLabel(
                ReadString(row, "supplierName", "SUPPLIER_NAME"),
                fallback: "غير محدد"),
            ["statusCode"] = statusCode,
            ["statusLabel"] = statusColor,
            ["currentStock"] = stock,
            ["daysOfCover"] = daysOfCover,
            ["suggestedOrderQty"] = netRequired,
            ["netSales30"] = ReadNumber(row, "netSales30Days", "NET_SOLD_30"),
            ["baseUnitLabel"] = SanitizeArabicLabel(
                ReadString(row, "baseUnitLabel", "BASE_UNIT_LABEL"),
                fallback: "وحدة أساسية"),
            ["purchaseUnitLabel"] = SanitizeArabicLabel(
                ReadString(row, "purchaseUnitLabel", "PURCHASE_UNIT_LABEL")),
            ["lastPurchasePrice"] = price,
            ["extras"] = extras,
        };
    }

    /// <summary>
    /// Maps shortage severity codes to traffic-light dots for the mobile UI.
    /// 0/1/2 → red, 3/4 → yellow, 5+ → green.
    /// </summary>
    private static string ResolveShortageDotColor(string? statusCode) =>
        statusCode?.Trim() switch
        {
            "0" or "1" or "2" => "red",
            "3" or "4" => "yellow",
            _ => "green",
        };

    /// <summary>
    /// Repairs known mojibake defaults that came from a mis-encoded SQL resource,
    /// and applies a clean Arabic fallback when the unit label is empty.
    /// </summary>
    public static string? SanitizeArabicLabel(string? value, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        // Known UTF-8→Latin-1 mojibake defaults from mis-encoded SQL resources.
        if (trimmed is "ÙˆØ­Ø¯Ø© Ø£Ø³Ø§Ø³ÙŠØ©" or "وحدة اساسية")
        {
            return "وحدة أساسية";
        }

        if (trimmed is "ØºÙŠØ± Ù…Ø­Ø¯Ø¯" or "غير محدد")
        {
            return "غير محدد";
        }

        // Labels that lost Arabic through a non-Unicode round-trip (mostly '?').
        if (!trimmed.Any(ch => ch is >= '\u0600' and <= '\u06FF') &&
            trimmed.Contains('?') &&
            fallback is not null)
        {
            return fallback;
        }

        // If the label still looks like Latin-1 mojibake of Arabic, try to repair.
        if (LooksLikeUtf8Mojibake(trimmed) && TryRepairUtf8Mojibake(trimmed, out var repaired))
        {
            return repaired;
        }

        return trimmed;
    }

    private static bool LooksLikeUtf8Mojibake(string value) =>
        value.Contains('Ø') || value.Contains('Ù') || value.Contains('Ã');

    private static bool TryRepairUtf8Mojibake(string value, out string repaired)
    {
        repaired = value;
        try
        {
            var latin1 = System.Text.Encoding.GetEncoding("ISO-8859-1");
            var bytes = latin1.GetBytes(value);
            var candidate = System.Text.Encoding.UTF8.GetString(bytes);
            if (candidate.Any(ch => ch is >= '\u0600' and <= '\u06FF') &&
                !candidate.Contains('\uFFFD'))
            {
                repaired = candidate;
                return true;
            }
        }
        catch
        {
            // Keep original value.
        }

        return false;
    }

    private static Dictionary<string, object?> MapCustomerDebtRow(
        Dictionary<string, object?> row,
        int index) =>
        new()
        {
            ["sortOrder"] = index,
            ["customerId"] = ReadString(row, "CustomerId", "customerId", "CUST_ID"),
            ["customerName"] = SanitizeArabicLabel(
                ReadString(row, "CustomerName", "customerName", "CUST_NAME")),
            ["phone"] = ReadString(row, "CustomerPhone", "customerPhone", "phone"),
            ["totalDebt"] = ReadNumber(row, "TotalDebt", "totalDebt"),
            ["invoiceCount"] = ReadNumber(row, "InvoiceCount", "invoiceCount"),
            ["lastInvoiceAt"] = ReadString(row, "LastInvoiceDate", "lastInvoiceDate", "lastInvoiceAt"),
            ["overdueAmount"] = null,
            ["extras"] = row,
        };

    private static Dictionary<string, object?> MapSupplierDebtRow(
        Dictionary<string, object?> row,
        int index) =>
        new()
        {
            ["sortOrder"] = index,
            ["supplierId"] = ReadString(row, "supplierId", "SupplierId"),
            ["supplierName"] = SanitizeArabicLabel(
                ReadString(row, "supplierName", "SupplierName"),
                fallback: "غير محدد"),
            ["debtAmount"] = ReadNumber(row, "debtAmount", "DebtAmount"),
            ["extras"] = row,
        };

    private static Dictionary<string, object?> MapExpiryRow(
        Dictionary<string, object?> row,
        int index)
    {
        var days = ReadNumber(row, "days_remaining", "daysRemaining");
        var daysInt = days is null ? (int?)null : (int)Math.Round(days.Value);
        var statusCode = daysInt is null
            ? "unknown"
            : daysInt < 0
                ? "expired"
                : "expiring_soon";

        return new Dictionary<string, object?>
        {
            ["sortOrder"] = index,
            ["itemId"] = ReadString(row, "item_id", "itemId"),
            ["itemName"] = SanitizeArabicLabel(
                ReadString(row, "item_name", "itemName"),
                fallback: "صنف")!,
            ["batchCode"] = null,
            ["expireDate"] = ReadString(row, "expiry_date", "expireDate", "expiryDate"),
            ["qty"] = ReadNumber(row, "quantity", "qty"),
            ["unitLabel"] = SanitizeArabicLabel(
                ReadString(row, "unit_name", "unitLabel", "unitName"),
                fallback: "وحدة أساسية"),
            ["daysRemaining"] = daysInt,
            ["statusCode"] = statusCode,
            ["extras"] = row,
        };
    }

    private static Dictionary<string, object?> MapInfinityExpiryRow(
        Dictionary<string, object?> row,
        int index) =>
        new()
        {
            ["sortOrder"] = index,
            ["productId"] = ReadString(row, "item_id", "productId", "ProductID_FK"),
            ["productCode"] = ReadString(row, "product_code", "productCode"),
            ["productName"] = SanitizeArabicLabel(
                ReadString(row, "item_name", "productName", "ProductName"),
                fallback: "صنف")!,
            ["expiryDate"] = ReadString(row, "expiry_date", "expiryDate"),
            ["quantity"] = ReadNumber(row, "quantity", "qty"),
            ["branchId"] = ReadNumber(row, "branch_id", "branchId"),
            ["branchName"] = SanitizeArabicLabel(ReadString(row, "branch_name", "branchName")),
            ["unitName"] = SanitizeArabicLabel(
                ReadString(row, "unit_name", "unitName"),
                fallback: "وحدة أساسية"),
            ["barcode"] = ReadString(row, "barcode"),
            ["supplierId"] = ReadString(row, "supplier_id", "supplierId"),
            ["supplierName"] = SanitizeArabicLabel(ReadString(row, "supplier_name", "supplierName")),
            ["locationsCount"] = 1,
            ["extras"] = new Dictionary<string, object?>
            {
                ["system"] = "infinity",
                ["daysRemaining"] = ReadNumber(row, "days_remaining", "daysRemaining"),
            },
        };

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

public sealed record SnapshotJobResult(
    string System,
    string SnapshotType,
    bool Success,
    int RowCount,
    string Message);

internal static class SnapshotSqlFiles
{
    public static string Read(string fileName)
    {
        var assembly = typeof(SnapshotSqlFiles).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name =>
                name.EndsWith($".{fileName}", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new FileNotFoundException($"ملف SQL غير مضمّن: {fileName}");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"تعذر فتح المورد: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
