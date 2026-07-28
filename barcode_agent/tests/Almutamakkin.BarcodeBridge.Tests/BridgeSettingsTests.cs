using System.Data.SqlClient;
using Almutamakkin.BarcodeBridge.Configuration;

namespace Almutamakkin.BarcodeBridge.Tests;

public sealed class BridgeSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "barcode-bridge-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildConnectionString_IsCompatibleWithSqlServer2005()
    {
        var settings = ValidSettings();
        var builder = new SqlConnectionStringBuilder(settings.BuildConnectionString());

        Assert.Equal("192.168.1.10", builder.DataSource);
        Assert.Equal("Marketing", builder.InitialCatalog);
        Assert.Equal("sa", builder.UserID);
        Assert.Equal("secret;value", builder.Password);
        Assert.False(builder.Encrypt);
        Assert.True(builder.TrustServerCertificate);
    }

    [Fact]
    public void EncryptedStore_RoundTripsWithDpapiWithoutPlainText()
    {
        var filePath = Path.Combine(_directory, "settings.dat");
        var store = new EncryptedSettingsStore(filePath);
        var expected = ValidSettings();

        store.Save(expected);
        var raw = File.ReadAllBytes(filePath);
        var loaded = store.LoadOrCreate();

        Assert.DoesNotContain("secret;value", System.Text.Encoding.UTF8.GetString(raw));
        Assert.Equal(expected.SqlServer, loaded.SqlServer);
        Assert.Equal(expected.Password, loaded.Password);
        Assert.Equal(expected.ApiKey, loaded.ApiKey);
        Assert.Equal(expected.PrinterName, loaded.PrinterName);
    }

    [Fact]
    public void ApiKeyGenerator_CreatesLongUrlSafeUniqueKeys()
    {
        var first = ApiKeyGenerator.Generate();
        var second = ApiKeyGenerator.Generate();

        Assert.True(first.Length >= 32);
        Assert.NotEqual(first, second);
        Assert.Matches("^[A-Za-z0-9_-]+$", first);
    }

    private static BridgeSettings ValidSettings() => new()
    {
        SqlServer = "192.168.1.10",
        Database = "Marketing",
        Username = "sa",
        Password = "secret;value",
        PrinterName = "Test Printer",
        Port = 8787,
        ApiKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
