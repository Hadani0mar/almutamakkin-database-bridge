using System.Text.Json.Serialization;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

public sealed class SnapshotFingerprintRecord
{
    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonPropertyName("lastCheckedUtc")]
    public string? LastCheckedUtc { get; set; }

    [JsonPropertyName("lastPublishedUtc")]
    public string? LastPublishedUtc { get; set; }

    [JsonPropertyName("rowCount")]
    public int RowCount { get; set; }
}

public interface ISnapshotFingerprintStore
{
    SnapshotFingerprintRecord? Get(string system, string snapshotType);

    void Set(
        string system,
        string snapshotType,
        string fingerprint,
        DateTime utcNow,
        int rowCount = 0,
        bool published = false);
}

public sealed class JsonSnapshotFingerprintStore : ISnapshotFingerprintStore
{
    private readonly object _sync = new();
    private Dictionary<string, SnapshotFingerprintRecord> _records;

    public JsonSnapshotFingerprintStore()
    {
        _records = LoadFromDisk();
    }

    public SnapshotFingerprintRecord? Get(string system, string snapshotType)
    {
        var key = Key(system, snapshotType);
        lock (_sync)
        {
            return _records.TryGetValue(key, out var record)
                ? Clone(record)
                : null;
        }
    }

    public void Set(
        string system,
        string snapshotType,
        string fingerprint,
        DateTime utcNow,
        int rowCount = 0,
        bool published = false)
    {
        var key = Key(system, snapshotType);
        lock (_sync)
        {
            _records.TryGetValue(key, out var existing);
            var next = new SnapshotFingerprintRecord
            {
                Fingerprint = fingerprint,
                LastCheckedUtc = utcNow.ToUniversalTime().ToString("O"),
                LastPublishedUtc = published
                    ? utcNow.ToUniversalTime().ToString("O")
                    : existing?.LastPublishedUtc,
                RowCount = rowCount > 0 ? rowCount : existing?.RowCount ?? 0,
            };
            _records[key] = next;
            SaveToDisk(_records);
        }
    }

    private static string Key(string system, string snapshotType) =>
        $"{system.Trim().ToLowerInvariant()}:{snapshotType.Trim().ToLowerInvariant()}";

    private static Dictionary<string, SnapshotFingerprintRecord> LoadFromDisk()
    {
        LabPaths.EnsureLocalAppDataRoot();
        if (!File.Exists(LabPaths.SnapshotFingerprintsFilePath))
        {
            return new Dictionary<string, SnapshotFingerprintRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(LabPaths.SnapshotFingerprintsFilePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, SnapshotFingerprintRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var document = BridgeJson.Deserialize<FingerprintDocument>(json);
        return document?.Records ??
               new Dictionary<string, SnapshotFingerprintRecord>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveToDisk(Dictionary<string, SnapshotFingerprintRecord> records)
    {
        LabPaths.EnsureLocalAppDataRoot();
        var document = new FingerprintDocument { Records = records };
        File.WriteAllText(LabPaths.SnapshotFingerprintsFilePath, BridgeJson.Serialize(document));
    }

    private static SnapshotFingerprintRecord Clone(SnapshotFingerprintRecord source) =>
        new()
        {
            Fingerprint = source.Fingerprint,
            LastCheckedUtc = source.LastCheckedUtc,
            LastPublishedUtc = source.LastPublishedUtc,
            RowCount = source.RowCount,
        };

    private sealed class FingerprintDocument
    {
        [JsonPropertyName("records")]
        public Dictionary<string, SnapshotFingerprintRecord> Records { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
