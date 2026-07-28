using System.Text.Json.Serialization;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

public sealed class JsonChangeCursorStore : IChangeCursorStore
{
    private readonly object _sync = new();
    private Dictionary<string, ChangeCursorRecord> _records;

    public JsonChangeCursorStore()
    {
        _records = LoadFromDisk();
    }

    public ChangeCursorRecord? Get(string system, string domain)
    {
        var key = Key(system, domain);
        lock (_sync)
        {
            return _records.TryGetValue(key, out var record) ? Clone(record) : null;
        }
    }

    public ChangeCursorRecord Touch(
        string system,
        string domain,
        string fingerprint,
        DateTime utcNow,
        string? watermarkJson = null)
    {
        var key = Key(system, domain);
        var nowText = utcNow.ToUniversalTime().ToString("O");

        lock (_sync)
        {
            _records.TryGetValue(key, out var existing);
            var changed = existing is null ||
                !string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal);

            var next = new ChangeCursorRecord
            {
                Revision = changed ? (existing?.Revision ?? 0) + 1 : existing?.Revision ?? 0,
                Fingerprint = fingerprint,
                WatermarkJson = watermarkJson ?? existing?.WatermarkJson,
                LastCheckedUtc = nowText,
                LastChangedUtc = changed ? nowText : existing?.LastChangedUtc,
            };

            _records[key] = next;
            SaveToDisk(_records);
            return Clone(next);
        }
    }

    private static string Key(string system, string domain) =>
        $"{system.Trim().ToLowerInvariant()}:{domain.Trim().ToLowerInvariant()}";

    private static Dictionary<string, ChangeCursorRecord> LoadFromDisk()
    {
        LabPaths.EnsureLocalAppDataRoot();
        if (!File.Exists(LabPaths.ChangeCursorsFilePath))
        {
            return new Dictionary<string, ChangeCursorRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(LabPaths.ChangeCursorsFilePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, ChangeCursorRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var document = BridgeJson.Deserialize<ChangeCursorDocument>(json);
        return document?.Records ??
               new Dictionary<string, ChangeCursorRecord>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveToDisk(Dictionary<string, ChangeCursorRecord> records)
    {
        LabPaths.EnsureLocalAppDataRoot();
        var document = new ChangeCursorDocument { Records = records };
        File.WriteAllText(LabPaths.ChangeCursorsFilePath, BridgeJson.Serialize(document));
    }

    private static ChangeCursorRecord Clone(ChangeCursorRecord source) =>
        new()
        {
            Revision = source.Revision,
            Fingerprint = source.Fingerprint,
            WatermarkJson = source.WatermarkJson,
            LastCheckedUtc = source.LastCheckedUtc,
            LastChangedUtc = source.LastChangedUtc,
        };

    private sealed class ChangeCursorDocument
    {
        [JsonPropertyName("records")]
        public Dictionary<string, ChangeCursorRecord> Records { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
