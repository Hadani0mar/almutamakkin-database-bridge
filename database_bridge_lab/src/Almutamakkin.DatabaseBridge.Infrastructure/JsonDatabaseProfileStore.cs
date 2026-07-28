using System.Text.Json.Serialization;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class JsonDatabaseProfileStore : IDatabaseProfileStore
{
    private readonly object _sync = new();
    private List<DatabaseProfile> _profiles;

    public JsonDatabaseProfileStore()
    {
        _profiles = LoadFromDisk();
    }

    public DatabaseProfile? GetByName(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        lock (_sync)
        {
            return _profiles.FirstOrDefault(
                profile => string.Equals(profile.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public DatabaseProfile? GetById(Guid id)
    {
        lock (_sync)
        {
            return _profiles.FirstOrDefault(profile => profile.Id == id);
        }
    }

    public IReadOnlyList<DatabaseProfile> GetAll()
    {
        lock (_sync)
        {
            return _profiles
                .Select(CloneProfile)
                .ToList();
        }
    }

    public void Save(DatabaseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Id == Guid.Empty)
        {
            profile.Id = Guid.NewGuid();
        }

        lock (_sync)
        {
            var existingIndex = _profiles.FindIndex(existing => existing.Id == profile.Id);
            if (existingIndex >= 0)
            {
                _profiles[existingIndex] = CloneProfile(profile);
            }
            else
            {
                _profiles.Add(CloneProfile(profile));
            }

            SaveToDisk(_profiles);
        }
    }

    public bool Delete(Guid id)
    {
        lock (_sync)
        {
            var removedCount = _profiles.RemoveAll(profile => profile.Id == id);
            if (removedCount == 0)
            {
                return false;
            }

            SaveToDisk(_profiles);
            return true;
        }
    }

    public void Reload()
    {
        lock (_sync)
        {
            _profiles = LoadFromDisk();
        }
    }

    private static List<DatabaseProfile> LoadFromDisk()
    {
        LabPaths.EnsureLocalAppDataRoot();

        if (!File.Exists(LabPaths.DatabaseProfilesFilePath))
        {
            return new List<DatabaseProfile>();
        }

        var json = File.ReadAllText(LabPaths.DatabaseProfilesFilePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<DatabaseProfile>();
        }

        var document = BridgeJson.Deserialize<DatabaseProfileDocument>(json);
        return document?.Profiles?.Select(CloneProfile).ToList() ?? new List<DatabaseProfile>();
    }

    private static void SaveToDisk(IReadOnlyList<DatabaseProfile> profiles)
    {
        LabPaths.EnsureLocalAppDataRoot();

        var document = new DatabaseProfileDocument
        {
            Profiles = profiles.Select(CloneProfile).ToList(),
        };

        var json = BridgeJson.Serialize(document);
        File.WriteAllText(LabPaths.DatabaseProfilesFilePath, json);
    }

    private static DatabaseProfile CloneProfile(DatabaseProfile profile) =>
        BridgeJson.Deserialize<DatabaseProfile>(BridgeJson.Serialize(profile))
        ?? throw new InvalidOperationException("Failed to clone database profile.");

    private sealed class DatabaseProfileDocument
    {
        [JsonPropertyName("profiles")]
        public List<DatabaseProfile> Profiles { get; set; } = new();
    }
}
