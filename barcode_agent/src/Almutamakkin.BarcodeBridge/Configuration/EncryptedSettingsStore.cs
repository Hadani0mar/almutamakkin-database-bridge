using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Almutamakkin.BarcodeBridge.Configuration;

public sealed class EncryptedSettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Almutamakkin.BarcodeBridge.Settings.v1");
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public EncryptedSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(GetDataDirectory(), "settings.dat");
    }

    public string DataDirectory => Path.GetDirectoryName(_filePath)!;

    public BridgeSettings LoadOrCreate()
    {
        if (!File.Exists(_filePath)) return new BridgeSettings();
        try
        {
            var encrypted = File.ReadAllBytes(_filePath);
            var clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            var settings = JsonSerializer.Deserialize<BridgeSettings>(clear, _json) ?? new BridgeSettings();
            if (string.IsNullOrWhiteSpace(settings.ApiKey) || settings.ApiKey.Length < 32)
                settings.ApiKey = ApiKeyGenerator.Generate();
            return settings;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException)
        {
            var damaged = _filePath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Move(_filePath, damaged, true); } catch (IOException) { }
            return new BridgeSettings();
        }
    }

    public void Save(BridgeSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var clear = JsonSerializer.SerializeToUtf8Bytes(settings, _json);
        var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        var temporary = _filePath + ".tmp";
        File.WriteAllBytes(temporary, encrypted);
        File.Move(temporary, _filePath, true);
    }

    public static string GetDataDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Almutamakkin",
        "BarcodeBridge");
}
