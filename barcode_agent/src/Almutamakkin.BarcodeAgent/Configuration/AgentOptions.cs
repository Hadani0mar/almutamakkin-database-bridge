using System.ComponentModel.DataAnnotations;

namespace Almutamakkin.BarcodeAgent.Configuration;

public sealed class ServerOptions
{
    public const string SectionName = "Server";
    [Required] public string Urls { get; init; } = "http://0.0.0.0:8787";
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    [Required] public string ConnectionString { get; init; } = string.Empty;
    [Range(1, 120)] public int CommandTimeoutSeconds { get; init; } = 15;
}

public sealed class PrinterOptions
{
    public const string SectionName = "Printer";
    [Required] public string QueueName { get; init; } = "Xprinter XP-365B Raw";
    [Range(10, 100)] public int LabelWidthMm { get; init; } = 38;
    [Range(10, 100)] public int LabelHeightMm { get; init; } = 25;
    [Range(100, 600)] public int Dpi { get; init; } = 203;
    [Range(1, 12)] public int Speed { get; init; } = 3;
    [Range(1, 15)] public int Density { get; init; } = 6;
    [Range(0, 20)] public int GapMm { get; init; } = 2;
    [Range(1, 100)] public int MaximumCopies { get; init; } = 20;
    [Range(1, 500)] public int QueueCapacity { get; init; } = 50;
    [Range(1, 120)] public int PrintRequestsPerMinute { get; init; } = 10;
    [Required] public string BusinessNameFont { get; init; } = "Tahoma";
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    [Required, MinLength(32)] public string ApiKey { get; init; } = string.Empty;
    public string HeaderName { get; init; } = "X-Almutamakkin-Key";
    public string[] AllowedNetworks { get; init; } =
    [
        "127.0.0.0/8",
        "::1/128",
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16"
    ];
}

public sealed class JobStoreOptions
{
    public const string SectionName = "JobStore";
    public string DataDirectory { get; init; } = "data";
    [Range(1, 168)] public int RetentionHours { get; init; } = 24;
}
