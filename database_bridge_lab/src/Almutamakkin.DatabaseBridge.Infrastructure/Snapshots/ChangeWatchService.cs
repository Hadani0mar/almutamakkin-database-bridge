using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

public sealed record ChangeWatchTickResult(
    string System,
    string SnapshotType,
    string DisplayName,
    bool Checked,
    bool Changed,
    bool Published,
    string Message,
    int RowCount = 0);

/// <summary>
/// Read-only change detection: cheap fingerprint SQL, publish full snapshot only on change.
/// Never writes to the user's SQL Server. Distinguishes Marketing vs Infinity plans.
/// </summary>
public sealed class ChangeWatchService
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly ISqlCommandExecutor _executor;
    private readonly AppSettings _settings;
    private readonly ActivitySnapshotSyncService _snapshotSync;
    private readonly ISnapshotFingerprintStore _fingerprints;
    private readonly IBridgeLogger _logger;

    private static readonly ChangeWatchPlan[] Plans =
    [
        // shortages / debts_* / expiry: phone pulls via bridge-relay (no Supabase publish).
        new("marketing", "shift_close_events", "أبوغريس · إغلاق الورديات", "marketing_fp_shift_close_events.sql", TimeSpan.FromSeconds(90), 30),
        new("marketing", "debt_invoice_events", "أبوغريس · أحداث الديون", "marketing_fp_debt_invoice_events.sql", TimeSpan.FromSeconds(90), 30),
        new("marketing", "business_profile", "أبوغريس · بيانات النشاط", "marketing_fp_business_profile.sql", TimeSpan.FromMinutes(30), 15),
        new("marketing", "sales_pattern", "أبوغريس · نمط البيع", "marketing_fp_sales_pattern.sql", TimeSpan.FromMinutes(15), 60),
        new("infinity", "business_profile", "إنفينيتي · بيانات الفرع", "infinity_fp_business_profile.sql", TimeSpan.FromMinutes(30), 15),
        new("infinity", "expiry", "إنفينيتي · الصلاحية", "infinity_fp_expiry.sql", TimeSpan.FromMinutes(10), 60),
    ];

    public ChangeWatchService(
        IDatabaseProfileStore profileStore,
        ISqlCommandExecutor executor,
        AppSettings settings,
        ActivitySnapshotSyncService snapshotSync,
        ISnapshotFingerprintStore fingerprints,
        IBridgeLogger logger)
    {
        _profileStore = profileStore;
        _executor = executor;
        _settings = settings;
        _snapshotSync = snapshotSync;
        _fingerprints = fingerprints;
        _logger = logger;
    }

    public IReadOnlyList<ChangeWatchPlan> WatchPlans => Plans;

    public async Task<IReadOnlyList<ChangeWatchTickResult>> TickAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<ChangeWatchTickResult>();
        if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
            string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            results.Add(new ChangeWatchTickResult(
                "all", "all", "المراقبة", false, false, false, "الجسر غير مسجّل."));
            return results;
        }

        var marketingProfile = ResolveMarketingProfile();
        var infinityProfile = ResolveInfinityProfile();
        if (marketingProfile is null && infinityProfile is null)
        {
            results.Add(new ChangeWatchTickResult(
                "all",
                "all",
                "المراقبة",
                false,
                false,
                false,
                "لا يوجد ملف اتصال Marketing أو InfinityRetailDB."));
            return results;
        }

        var now = DateTime.UtcNow;
        foreach (var plan in Plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = string.Equals(plan.System, "infinity", StringComparison.OrdinalIgnoreCase)
                ? infinityProfile
                : marketingProfile;
            if (profile is null)
            {
                continue;
            }

            try
            {
                results.Add(await EvaluatePlanAsync(profile, plan, now, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.Error($"change-watch {plan.System}/{plan.SnapshotType} failed: {ex.Message}", ex);
                results.Add(new ChangeWatchTickResult(
                    plan.System,
                    plan.SnapshotType,
                    plan.DisplayName,
                    true,
                    false,
                    false,
                    ex.Message));
            }
        }

        if (results.Count == 0)
        {
            results.Add(new ChangeWatchTickResult(
                "all",
                "all",
                "المراقبة",
                false,
                false,
                false,
                "لا خطط مراقبة جاهزة للنظام المتصل."));
        }

        return results;
    }

    private async Task<ChangeWatchTickResult> EvaluatePlanAsync(
        DatabaseProfile profile,
        ChangeWatchPlan plan,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var previous = _fingerprints.Get(plan.System, plan.SnapshotType);
        if (previous?.LastCheckedUtc is not null &&
            DateTime.TryParse(
                previous.LastCheckedUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var lastChecked) &&
            nowUtc - lastChecked.ToUniversalTime() < plan.Interval)
        {
            return new ChangeWatchTickResult(
                plan.System,
                plan.SnapshotType,
                plan.DisplayName,
                false,
                false,
                false,
                "لم يحن موعد الفحص.");
        }

        var fingerprint = await ReadFingerprintAsync(profile, plan, cancellationToken)
            .ConfigureAwait(false);
        if (fingerprint is null)
        {
            _fingerprints.Set(plan.System, plan.SnapshotType, "error", nowUtc, published: false);
            return new ChangeWatchTickResult(
                plan.System,
                plan.SnapshotType,
                plan.DisplayName,
                true,
                false,
                false,
                "فشل استعلام البصمة.");
        }

        if (previous is not null &&
            string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            _fingerprints.Set(
                plan.System,
                plan.SnapshotType,
                fingerprint,
                nowUtc,
                rowCount: previous.RowCount,
                published: false);
            return new ChangeWatchTickResult(
                plan.System,
                plan.SnapshotType,
                plan.DisplayName,
                true,
                false,
                false,
                "لا تغيير — تخطي النشر.");
        }

        var publish = string.Equals(plan.System, "infinity", StringComparison.OrdinalIgnoreCase)
            ? await _snapshotSync.PublishInfinityTypeAsync(plan.SnapshotType, cancellationToken)
                .ConfigureAwait(false)
            : await _snapshotSync.PublishMarketingTypeAsync(plan.SnapshotType, cancellationToken)
                .ConfigureAwait(false);

        if (publish.Success)
        {
            _fingerprints.Set(
                plan.System,
                plan.SnapshotType,
                fingerprint,
                nowUtc,
                rowCount: publish.RowCount,
                published: true);
            _logger.Info(
                $"change-watch publish {plan.System}/{plan.SnapshotType}: rows={publish.RowCount} fp={fingerprint[..Math.Min(12, fingerprint.Length)]}");
        }
        else
        {
            _fingerprints.Set(plan.System, plan.SnapshotType, fingerprint, nowUtc, published: false);
            _logger.Warning(
                $"change-watch publish failed {plan.System}/{plan.SnapshotType}: {publish.Message}");
        }

        return new ChangeWatchTickResult(
            plan.System,
            plan.SnapshotType,
            plan.DisplayName,
            true,
            true,
            publish.Success,
            publish.Success
                ? $"نُشر بعد تغيير ({publish.RowCount})"
                : publish.Message,
            publish.RowCount);
    }

    private async Task<string?> ReadFingerprintAsync(
        DatabaseProfile profile,
        ChangeWatchPlan plan,
        CancellationToken cancellationToken)
    {
        var sql = SnapshotSqlFiles.Read(plan.FingerprintSqlFile);
        var execution = await _executor.ExecuteAsync(
            profile,
            new SqlExecutePayload
            {
                DatabaseProfile = profile.ProfileName,
                Sql = sql,
                TimeoutSeconds = plan.FingerprintTimeoutSeconds,
                MaxRows = 5,
            },
            cancellationToken).ConfigureAwait(false);

        if (!execution.Success)
        {
            _logger.Warning(
                $"change-watch fingerprint {plan.System}/{plan.SnapshotType}: {execution.ErrorMessage}");
            return null;
        }

        var row = execution.ResultSets.FirstOrDefault()?.Rows.FirstOrDefault();
        if (row is null || row.Count == 0)
        {
            return "empty";
        }

        return HashRow(row);
    }

    private DatabaseProfile? ResolveMarketingProfile() =>
        ResolveSystemProfile(
            _settings.SnapshotMarketingProfileName,
            "Marketing");

    private DatabaseProfile? ResolveInfinityProfile() =>
        ResolveSystemProfile(
            _settings.SnapshotInfinityProfileName,
            "InfinityRetailDB");

    private DatabaseProfile? ResolveSystemProfile(string? preferredName, string canonicalName)
    {
        var all = _profileStore.GetAll().Where(profile => profile.IsEnabled).ToList();
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var preferred = all.FirstOrDefault(profile =>
                string.Equals(
                    profile.ProfileName,
                    preferredName,
                    StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        var remoteNamed = all.FirstOrDefault(profile =>
            string.Equals(
                profile.ProfileName,
                ActivitySnapshotSyncService.ToRemoteProfileName(canonicalName),
                StringComparison.OrdinalIgnoreCase));
        if (remoteNamed is not null)
        {
            return remoteNamed;
        }

        return all.FirstOrDefault(profile =>
            string.Equals(profile.ProfileName, canonicalName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.DatabaseName, canonicalName, StringComparison.OrdinalIgnoreCase));
    }

    private static string HashRow(Dictionary<string, object?> row)
    {
        var parts = row
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={FormatValue(pair.Value)}");
        var payload = string.Join("|", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static string FormatValue(object? value)
    {
        if (value is null || value is DBNull)
        {
            return string.Empty;
        }

        return value switch
        {
            DateTime dt => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }
}

public sealed record ChangeWatchPlan(
    string System,
    string SnapshotType,
    string DisplayName,
    string FingerprintSqlFile,
    TimeSpan Interval,
    int FingerprintTimeoutSeconds);
