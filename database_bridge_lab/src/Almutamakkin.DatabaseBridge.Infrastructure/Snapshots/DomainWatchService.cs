using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

public sealed record DomainWatchTickResult(
    string System,
    string Domain,
    string DisplayName,
    bool Enabled,
    bool Checked,
    bool Changed,
    long Revision,
    string Message,
    long DurationMs = 0);

public sealed record DomainWatchPlan(
    string System,
    string Domain,
    string DisplayName,
    string FingerprintSqlFile,
    int FingerprintTimeoutSeconds,
    Func<AppSettings, bool> IsEnabled)
{
    /// <summary>
    /// Builds the Infrastructure-only fingerprint-SQL mapping on top of the
    /// shared Core catalog (System/Domain/DisplayName/IsEnabled), so both
    /// sides describe the same domain without duplicating that part.
    /// </summary>
    public static DomainWatchPlan FromDescriptor(
        ChangeDomainDescriptor descriptor,
        string fingerprintSqlFile,
        int fingerprintTimeoutSeconds) =>
        new(
            descriptor.System,
            descriptor.Domain,
            descriptor.DisplayName,
            fingerprintSqlFile,
            fingerprintTimeoutSeconds,
            descriptor.IsEnabled);
}

/// <summary>
/// Phase 0/1 change-stream foundation: watch cheap → ticket/delta later.
/// Runs fingerprint SQL only (never the full snapshot SQL) and bumps a local
/// revision in <see cref="IChangeCursorStore"/> when the fingerprint changes.
/// Never calls ActivitySnapshotSyncService publish APIs — those stay on
/// <see cref="ChangeWatchService"/>. Supports Marketing and Infinity profiles.
/// No-op end to end while <see cref="AppSettings.EnableChangeStreamWatch"/>
/// is false (the default), so current behavior is unchanged until an
/// operator opts in.
/// </summary>
public sealed class DomainWatchService
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly ISqlCommandExecutor _executor;
    private readonly AppSettings _settings;
    private readonly IChangeCursorStore _cursorStore;
    private readonly ISecretProtector _secretProtector;
    private readonly SupabaseChangeTicketClient _ticketClient;
    private readonly IBridgeLogger _logger;

    private static readonly IReadOnlyDictionary<string, (string SqlFile, int TimeoutSeconds)> DomainSqlFiles =
        new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["marketing:debt_invoice_events"] = ("marketing_fp_debt_invoice_events.sql", 30),
            ["marketing:shift_close_events"] = ("marketing_fp_shift_close_events.sql", 30),
            ["infinity:purchase_invoice_events"] = ("infinity_fp_purchase_invoice_events.sql", 30),
            ["infinity:sales_invoice_events"] = ("infinity_fp_sales_invoice_events.sql", 30),
            ["infinity:expiry"] = ("infinity_fp_expiry.sql", 45),
        };

    public static readonly IReadOnlyList<DomainWatchPlan> Plans =
        ChangeDomainCatalog.Domains
            .Select(descriptor =>
            {
                var (sqlFile, timeoutSeconds) = DomainSqlFiles[$"{descriptor.System}:{descriptor.Domain}"];
                return DomainWatchPlan.FromDescriptor(descriptor, sqlFile, timeoutSeconds);
            })
            .ToList();

    public DomainWatchService(
        IDatabaseProfileStore profileStore,
        ISqlCommandExecutor executor,
        AppSettings settings,
        IChangeCursorStore cursorStore,
        ISecretProtector secretProtector,
        SupabaseChangeTicketClient ticketClient,
        IBridgeLogger logger)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cursorStore = cursorStore ?? throw new ArgumentNullException(nameof(cursorStore));
        _secretProtector = secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));
        _ticketClient = ticketClient ?? throw new ArgumentNullException(nameof(ticketClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static DomainWatchPlan? FindPlan(string system, string domain) =>
        Plans.FirstOrDefault(plan =>
            string.Equals(plan.System, system, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(plan.Domain, domain, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<DomainWatchTickResult>> TickAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<DomainWatchTickResult>();

        if (!_settings.EnableChangeStreamWatch)
        {
            foreach (var plan in Plans)
            {
                results.Add(new DomainWatchTickResult(
                    plan.System, plan.Domain, plan.DisplayName,
                    Enabled: false, Checked: false, Changed: false, Revision: 0,
                    Message: "مراقبة الدلتا متوقفة (EnableChangeStreamWatch=false)."));
            }

            return results;
        }

        var activeProfile = ResolveActiveProfile();
        var activeSystem = GetSystem(activeProfile);
        if (activeProfile is null || activeSystem is null)
        {
            _logger.Warning("domain-watch skipped: no enabled active Marketing or InfinityRetailDB profile.");
            return results;
        }

        var tickSw = Stopwatch.StartNew();

        foreach (var plan in Plans.Where(plan => string.Equals(plan.System, activeSystem, StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!plan.IsEnabled(_settings))
            {
                results.Add(new DomainWatchTickResult(
                    plan.System, plan.Domain, plan.DisplayName,
                    Enabled: false, Checked: false, Changed: false,
                    Revision: _cursorStore.Get(plan.System, plan.Domain)?.Revision ?? 0,
                    Message: "مراقبة هذا النطاق متوقفة."));
                continue;
            }

            var result = await EvaluatePlanAsync(activeProfile, plan, cancellationToken)
                .ConfigureAwait(false);
            results.Add(result);
        }

        tickSw.Stop();
        var durationMs = tickSw.ElapsedMilliseconds;
        if (durationMs > _settings.ChangeWatchBudgetMs)
        {
            _logger.Warning(
                $"domain-watch tick exceeded budget: {durationMs}ms > {_settings.ChangeWatchBudgetMs}ms " +
                $"({results.Count(r => r.Checked)} checked).");
        }
        else
        {
            _logger.Info($"domain-watch tick: {durationMs}ms ({results.Count(r => r.Checked)} checked).");
        }

        return results;
    }

    private async Task<DomainWatchTickResult> EvaluatePlanAsync(
        DatabaseProfile profile,
        DomainWatchPlan plan,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
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

            sw.Stop();

            if (!execution.Success)
            {
                _logger.Warning(
                    $"domain-watch fingerprint {plan.System}/{plan.Domain}: {execution.ErrorMessage}");
                var lastKnown = _cursorStore.Get(plan.System, plan.Domain);
                return new DomainWatchTickResult(
                    plan.System, plan.Domain, plan.DisplayName,
                    Enabled: true, Checked: false, Changed: false,
                    Revision: lastKnown?.Revision ?? 0,
                    Message: $"فشل استعلام البصمة: {execution.ErrorMessage}",
                    DurationMs: sw.ElapsedMilliseconds);
            }

            var row = execution.ResultSets.FirstOrDefault()?.Rows.FirstOrDefault();
            var fingerprint = row is null || row.Count == 0 ? "empty" : HashRow(row);

            var previousRevision = _cursorStore.Get(plan.System, plan.Domain)?.Revision ?? 0;
            var cursor = _cursorStore.Touch(plan.System, plan.Domain, fingerprint, DateTime.UtcNow);
            var changed = cursor.Revision != previousRevision;

            var message = changed
                ? $"تغيّر — مراجعة جديدة #{cursor.Revision}"
                : $"بلا تغيير — المراجعة الحالية #{cursor.Revision}";

            if (changed)
            {
                var publishNote = await TryPublishTicketAsync(
                        plan.System,
                        plan.Domain,
                        cursor.Revision,
                        previousRevision,
                        fingerprint,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(publishNote))
                {
                    message = $"{message} · {publishNote}";
                }
            }

            return new DomainWatchTickResult(
                plan.System, plan.Domain, plan.DisplayName,
                Enabled: true, Checked: true, Changed: changed,
                Revision: cursor.Revision,
                Message: message,
                DurationMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Error($"domain-watch {plan.System}/{plan.Domain} failed: {ex.Message}", ex);
            var lastKnown = _cursorStore.Get(plan.System, plan.Domain);
            return new DomainWatchTickResult(
                plan.System, plan.Domain, plan.DisplayName,
                Enabled: true, Checked: false, Changed: false,
                Revision: lastKnown?.Revision ?? 0,
                Message: ex.Message,
                DurationMs: sw.ElapsedMilliseconds);
        }
    }

    private async Task<string?> TryPublishTicketAsync(
        string system,
        string domain,
        long revision,
        long previousRevision,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            return "لم يُنشر — الجسر غير مسجّل";
        }

        try
        {
            var deviceSecret = _secretProtector.Unprotect(_settings.EncryptedDeviceSecret!);
            var result = await _ticketClient.PublishAsync(
                    _settings,
                    deviceSecret,
                    system,
                    domain,
                    revision,
                    previousRevision,
                    fingerprint,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.Success ? "نُشرت تذكرة" : result.Message;
        }
        catch (Exception ex)
        {
            _logger.Warning($"change-ticket publish skipped: {ex.Message}");
            return $"فشل النشر: {ex.Message}";
        }
    }

    private DatabaseProfile? ResolveActiveProfile()
    {
        if (string.IsNullOrWhiteSpace(_settings.ActiveDatabaseProfileName))
        {
            return null;
        }

        var active = _profileStore.GetByName(_settings.ActiveDatabaseProfileName);
        return active is { IsEnabled: true } ? active : null;
    }

    private static string? GetSystem(DatabaseProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        return string.Equals(profile.DatabaseName, "Marketing", StringComparison.OrdinalIgnoreCase)
            ? "marketing"
            : string.Equals(profile.DatabaseName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase)
                ? "infinity"
                : null;
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
