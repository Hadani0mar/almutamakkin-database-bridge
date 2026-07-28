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

        // The selected local/network connection is retained independently for
        // each sales system.  A bridge may have both systems and both transport
        // types configured at the same time, so the dashboard selection alone
        // is not sufficient routing state.
        var systemSelection = GetSystemSelection(requestedSystem);
        var explicitlySelected = FindSingleProfile(
            enabledProfiles,
            systemSelection,
            requestedSystem);
        if (explicitlySelected is not null)
        {
            return explicitlySelected;
        }

        // Prefer the operator-selected live profile when it belongs to the
        // requested sales system. A bridge can legitimately host both
        // Marketing and Infinity profiles, however, so a selected Marketing
        // profile must not make an Infinity request fail (and vice versa).
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

            if (active.Count == 1 &&
                string.Equals(GetSystem(active[0]), requestedSystem, StringComparison.Ordinal))
            {
                return active[0];
            }

            if (active.Count == 1)
            {
                // Legacy settings have one global selection.  When it belongs
                // to the other sales system, use only the requested-system
                // profile with the same connection kind.  This preserves the
                // operator's local-versus-network choice without falling back
                // to the first enabled profile.
                var sameConnectionKind = enabledProfiles
                    .Where(profile => string.Equals(GetSystem(profile), requestedSystem, StringComparison.Ordinal))
                    .Where(profile => profile.ConnectionKind == active[0].ConnectionKind)
                    .Take(2)
                    .ToList();

                return sameConnectionKind.Count == 1 ? sameConnectionKind[0] : null;
            }

            // A second sales system is requested while another one is active,
            // or the persisted active selection refers to a profile that has
            // since been renamed/removed. Resolve only an unambiguous enabled
            // profile of the requested system. This is never a cross-system or
            // "first enabled profile" fallback.
            var systemProfile = enabledProfiles
                .Where(profile => string.Equals(GetSystem(profile), requestedSystem, StringComparison.Ordinal))
                .Take(2)
                .ToList();

            return systemProfile.Count == 1 ? systemProfile[0] : null;
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

    private string? GetSystemSelection(string system) => system switch
    {
        "marketing" => _settings.ActiveMarketingDatabaseProfileName?.Trim(),
        "infinity" => _settings.ActiveInfinityDatabaseProfileName?.Trim(),
        _ => null,
    };

    private DatabaseProfile? FindSingleProfile(
        IReadOnlyCollection<DatabaseProfile> profiles,
        string? profileName,
        string requestedSystem)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return null;
        }

        var matches = profiles
            .Where(profile => string.Equals(
                profile.ProfileName,
                profileName,
                StringComparison.OrdinalIgnoreCase))
            .Where(profile => string.Equals(GetSystem(profile), requestedSystem, StringComparison.Ordinal))
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
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
