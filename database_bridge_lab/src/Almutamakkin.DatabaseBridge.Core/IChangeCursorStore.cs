using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// Local cursor for one watched (system, domain) pair. Revision only bumps
/// when a domain's cheap fingerprint SQL result changes; watermark is an
/// optional opaque JSON blob (e.g. max id / max timestamp) domains can use
/// later to resume a real ticket/delta pull without re-fingerprinting from
/// scratch. Interface lives in Core (like <see cref="IDatabaseProfileStore"/>)
/// so both the changes.probe/changes.pull handlers and the Infrastructure
/// watch service can share it without a circular project reference.
/// </summary>
public sealed class ChangeCursorRecord
{
    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonPropertyName("watermarkJson")]
    public string? WatermarkJson { get; set; }

    [JsonPropertyName("lastCheckedUtc")]
    public string? LastCheckedUtc { get; set; }

    [JsonPropertyName("lastChangedUtc")]
    public string? LastChangedUtc { get; set; }
}

public interface IChangeCursorStore
{
    ChangeCursorRecord? Get(string system, string domain);

    /// <summary>
    /// Records the result of one cheap fingerprint check. Bumps Revision
    /// only when <paramref name="fingerprint"/> differs from the stored
    /// value (or no record exists yet). Never touches SQL Server itself —
    /// callers must have already computed the fingerprint.
    /// </summary>
    ChangeCursorRecord Touch(
        string system,
        string domain,
        string fingerprint,
        DateTime utcNow,
        string? watermarkJson = null);
}
