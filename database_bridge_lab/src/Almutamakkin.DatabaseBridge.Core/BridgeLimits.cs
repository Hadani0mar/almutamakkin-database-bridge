namespace Almutamakkin.DatabaseBridge.Core;

public static class BridgeLimits
{
    public const string SupportedProtocolVersion = "1.0";

    public const int MaxSqlLength = 100_000;
    public const int DefaultTimeoutSeconds = 30;
    // Heavy snapshot jobs (product_search / daily stats) need longer SQL windows.
    public const int MaximumTimeoutSeconds = 600;
    public const int DefaultMaxRows = 1_000;
    public const int MaximumMaxRows = 30_000;
    public const int MaximumRequestAgeMinutes = 5;
    public const int MaximumConcurrentQueries = 2;
    public const int MaximumResponseSizeMb = 32;
    public const int ProcessedRequestRetentionHours = 24;
}
