using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class AppSettingsStore
{
    public AppSettings Load()
    {
        LabPaths.EnsureLocalAppDataRoot();

        if (!File.Exists(LabPaths.AppSettingsFilePath))
        {
            var defaults = CreateDefaults();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(LabPaths.AppSettingsFilePath);
            var settings = BridgeJson.Deserialize<AppSettings>(json);
            return Normalize(settings ?? CreateDefaults());
        }
        catch
        {
            return CreateDefaults();
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SupabaseUrl))
        {
            settings.SupabaseUrl = SupabaseBridgeTransport.DefaultSupabaseFunctionsBaseUrl;
        }
        else
        {
            settings.SupabaseUrl = settings.SupabaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(settings.AnonKey))
        {
            settings.AnonKey = SupabaseBridgeTransport.DefaultAnonKey;
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        LabPaths.EnsureLocalAppDataRoot();
        var json = BridgeJson.Serialize(settings);
        File.WriteAllText(LabPaths.AppSettingsFilePath, json);
    }

    private static AppSettings CreateDefaults() =>
        new()
        {
            TunnelId = "LAB-TNL-001",
            TransportMode = TransportMode.SupabaseTunnel,
            SupabaseUrl = SupabaseBridgeTransport.DefaultSupabaseFunctionsBaseUrl,
            AnonKey = SupabaseBridgeTransport.DefaultAnonKey,
        };
}
