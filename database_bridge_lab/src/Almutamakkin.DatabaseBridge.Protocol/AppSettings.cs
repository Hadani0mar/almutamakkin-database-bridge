using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public enum TransportMode
{
    LocalTest,
    WebSocket,
    SupabaseTunnel,
}

public sealed class AppSettings
{
    [JsonPropertyName("tunnelId")]
    public string TunnelId { get; set; } = "LAB-TNL-001";

    [JsonPropertyName("transportMode")]
    public TransportMode TransportMode { get; set; } = TransportMode.LocalTest;

    [JsonPropertyName("webSocketUrl")]
    public string? WebSocketUrl { get; set; }

    [JsonPropertyName("supabaseUrl")]
    public string? SupabaseUrl { get; set; }

    [JsonPropertyName("anonKey")]
    public string? AnonKey { get; set; }

    [JsonPropertyName("encryptedDeviceSecret")]
    public string? EncryptedDeviceSecret { get; set; }

    /// <summary>
    /// Offline publisher public key used to verify server-owned query packages.
    /// The matching private key is never stored in the bridge or mobile app.
    /// </summary>
    [JsonPropertyName("queryPackageSigningPublicKeyPem")]
    public string? QueryPackageSigningPublicKeyPem { get; set; } = QueryPackageTrustAnchor.PublicKeyPem;

    [JsonPropertyName("queryPackageSigningKeyId")]
    public string? QueryPackageSigningKeyId { get; set; } = QueryPackageTrustAnchor.KeyId;

    [JsonPropertyName("lastPairingCode")]
    public string? LastPairingCode { get; set; }

    [JsonPropertyName("lastPairingExpiresAtUtc")]
    public string? LastPairingExpiresAtUtc { get; set; }

    /// <summary>
    /// Profile the operator selected in the UI; used when the app asks for a
    /// canonical name that is missing or when rebinding Marketing.
    /// </summary>
    [JsonPropertyName("activeDatabaseProfileName")]
    public string? ActiveDatabaseProfileName { get; set; }

    /// <summary>
    /// Optional profile used only for cloud snapshot sync (must not overwrite local Marketing).
    /// </summary>
    [JsonPropertyName("snapshotMarketingProfileName")]
    public string? SnapshotMarketingProfileName { get; set; }

    /// <summary>
    /// Optional profile used only for Infinity cloud snapshot sync.
    /// </summary>
    [JsonPropertyName("snapshotInfinityProfileName")]
    public string? SnapshotInfinityProfileName { get; set; }

    [JsonPropertyName("logFullSql")]
    public bool LogFullSql { get; set; } = true;

    [JsonPropertyName("maxConcurrentQueries")]
    public int MaxConcurrentQueries { get; set; } = 2;

    [JsonPropertyName("defaultTimeoutSeconds")]
    public int DefaultTimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("maximumTimeoutSeconds")]
    public int MaximumTimeoutSeconds { get; set; } = 600;

    [JsonPropertyName("defaultMaxRows")]
    public int DefaultMaxRows { get; set; } = 1000;

    [JsonPropertyName("maximumMaxRows")]
    public int MaximumMaxRows { get; set; } = 30000;

    [JsonPropertyName("maximumResponseSizeMb")]
    public int MaximumResponseSizeMb { get; set; } = 32;

    [JsonPropertyName("maxSqlLength")]
    public int MaxSqlLength { get; set; } = 100_000;

    [JsonPropertyName("maximumRequestAgeMinutes")]
    public int MaximumRequestAgeMinutes { get; set; } = 5;

    [JsonPropertyName("processedRequestRetentionHours")]
    public int ProcessedRequestRetentionHours { get; set; } = 24;

    /// <summary>
    /// Phase 0/1 change-stream foundation. Enabled by default so MainForm
    /// starts the change-watch timer when the bridge starts.
    /// Enabling this alone does nothing for a specific domain unless the
    /// matching Enable*ChangeStream flag below is also true.
    /// </summary>
    [JsonPropertyName("enableChangeStreamWatch")]
    public bool EnableChangeStreamWatch { get; set; } = true;

    /// <summary>
    /// Cheap fingerprint-only watch for marketing_fp_debt_invoice_events.sql.
    /// Never calls ActivitySnapshotSyncService.PublishMarketingTypeAsync.
    /// </summary>
    [JsonPropertyName("enableDebtInvoiceChangeStream")]
    public bool EnableDebtInvoiceChangeStream { get; set; } = true;

    /// <summary>
    /// Cheap fingerprint-only watch for marketing_fp_shift_close_events.sql.
    /// Never calls ActivitySnapshotSyncService.PublishMarketingTypeAsync.
    /// </summary>
    [JsonPropertyName("enableShiftCloseChangeStream")]
    public bool EnableShiftCloseChangeStream { get; set; } = true;

    /// <summary>
    /// Cheap fingerprint-only watch for infinity_fp_purchase_invoice_events.sql.
    /// </summary>
    [JsonPropertyName("enableInfinityPurchaseInvoiceChangeStream")]
    public bool EnableInfinityPurchaseInvoiceChangeStream { get; set; } = true;

    /// <summary>
    /// Cheap fingerprint-only watch for infinity_fp_sales_invoice_events.sql.
    /// </summary>
    [JsonPropertyName("enableInfinitySalesInvoiceChangeStream")]
    public bool EnableInfinitySalesInvoiceChangeStream { get; set; } = true;

    /// <summary>
    /// Cheap fingerprint-only watch for infinity_fp_expiry.sql (delta tickets).
    /// Snapshot publish for expiry remains on ChangeWatchService.
    /// </summary>
    [JsonPropertyName("enableInfinityExpiryChangeStream")]
    public bool EnableInfinityExpiryChangeStream { get; set; } = true;

    [JsonPropertyName("changeWatchIntervalSeconds")]
    public int ChangeWatchIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Soft budget (ms) logged per domain-watch tick; does not abort the tick,
    /// used only to flag slow fingerprint queries in the log.
    /// </summary>
    [JsonPropertyName("changeWatchBudgetMs")]
    public int ChangeWatchBudgetMs { get; set; } = 300;

    /// <summary>
    /// Product-photo writes are enabled by default for the operator-managed
    /// bridge. The handler still validates the exact allowed target system.
    /// </summary>
    [JsonPropertyName("enableInfinityProductPhotoWrite")]
    public bool EnableInfinityProductPhotoWrite { get; set; } = true;

    /// <summary>
    /// Product-photo writes are enabled by default for the operator-managed
    /// bridge. The handler still validates the exact allowed target system.
    /// </summary>
    [JsonPropertyName("enableMarketingProductPhotoWrite")]
    public bool EnableMarketingProductPhotoWrite { get; set; } = true;
}
