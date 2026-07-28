using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// Resolves only the one database profile selected for live phone requests.
/// Snapshot profiles and similarly named profiles are never routing fallbacks.
/// </summary>
public interface ILiveDatabaseProfileResolver
{
    DatabaseProfile? Resolve(string? requestedProfileName);

    string? GetSystem(string? requestedProfileName);

    string GetSystem(DatabaseProfile profile);
}

public sealed class LiveDatabaseProfileResolver : ILiveDatabaseProfileResolver
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly AppSettings _settings;

    public LiveDatabaseProfileResolver(
        IDatabaseProfileStore profileStore,
        AppSettings settings)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public DatabaseProfile? Resolve(string? requestedProfileName)
    {
        var requestedSystem = GetSystem(requestedProfileName);
        if (requestedSystem is null)
        {
            return null;
        }

        var enabledProfiles = _profileStore.GetAll()
            .Where(profile => profile.IsEnabled)
            .ToList();

        // Live phone traffic is bound to the exact profile selected by the
        // bridge operator. Never fall through to another enabled profile,
        // even when it belongs to the same system: that could expose a second
        // business, a stale backup, or a remote connection by accident.
        var activeName = _settings.ActiveDatabaseProfileName?.Trim();
        if (!string.IsNullOrWhiteSpace(activeName))
        {
            var active = enabledProfiles
                .Where(profile => string.Equals(
                    profile.ProfileName,
                    activeName,
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();

            if (active.Count != 1)
            {
                return null;
            }

            return string.Equals(GetSystem(active[0]), requestedSystem, StringComparison.Ordinal)
                ? active[0]
                : null;
        }

        // Without an explicit selection preserve the migration-safe legacy
        // rule: only an exact canonical local profile is eligible.
        var canonical = enabledProfiles
            .Where(profile => profile.ConnectionKind == DatabaseConnectionKind.Local)
            .Where(profile => string.Equals(
                profile.ProfileName,
                requestedProfileName?.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .Where(profile => string.Equals(GetSystem(profile), requestedSystem, StringComparison.Ordinal))
            .Take(2)
            .ToList();

        return canonical.Count == 1 ? canonical[0] : null;
    }

    public string? GetSystem(string? requestedProfileName)
    {
        var requested = requestedProfileName?.Trim();
        return requested switch
        {
            var value when string.Equals(value, "Marketing", StringComparison.OrdinalIgnoreCase) => "marketing",
            var value when string.Equals(value, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase) => "infinity",
            _ => null,
        };
    }

    public string GetSystem(DatabaseProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.Equals(profile.DatabaseName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase) ||
            profile.ProfileName.StartsWith("InfinityRetailDB", StringComparison.OrdinalIgnoreCase))
        {
            return "infinity";
        }

        // Marketing live/backup profiles stay on the marketing system even when
        // DatabaseName points at an old same-schema backup catalog.
        if (string.Equals(profile.DatabaseName, "Marketing", StringComparison.OrdinalIgnoreCase) ||
            profile.ProfileName.StartsWith("Marketing", StringComparison.OrdinalIgnoreCase))
        {
            return "marketing";
        }

        return profile.DatabaseName.Trim().ToLowerInvariant();
    }
}
