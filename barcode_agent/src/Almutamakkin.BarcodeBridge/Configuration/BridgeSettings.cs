using System.Data.SqlClient;
using System.Security.Cryptography;

namespace Almutamakkin.BarcodeBridge.Configuration;

public sealed class BridgeSettings
{
    public string SqlServer { get; set; } = "127.0.0.1";
    public string Database { get; set; } = "Marketing";
    public string Username { get; set; } = "sa";
    public string Password { get; set; } = string.Empty;
    public string PrinterName { get; set; } = string.Empty;
    public int Port { get; set; } = 8787;
    public string ApiKey { get; set; } = ApiKeyGenerator.Generate();
    public bool RunAtStartup { get; set; }

    public BridgeSettings Copy() => new()
    {
        SqlServer = SqlServer,
        Database = Database,
        Username = Username,
        Password = Password,
        PrinterName = PrinterName,
        Port = Port,
        ApiKey = ApiKey,
        RunAtStartup = RunAtStartup
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SqlServer)) errors.Add("عنوان خادم قاعدة البيانات مطلوب.");
        if (string.IsNullOrWhiteSpace(Database)) errors.Add("اسم قاعدة البيانات مطلوب.");
        if (string.IsNullOrWhiteSpace(Username)) errors.Add("اسم مستخدم قاعدة البيانات مطلوب.");
        if (string.IsNullOrWhiteSpace(Password)) errors.Add("كلمة مرور قاعدة البيانات مطلوبة.");
        if (string.IsNullOrWhiteSpace(PrinterName)) errors.Add("اختر طابعة باركود.");
        if (Port is < 1024 or > 65535) errors.Add("المنفذ يجب أن يكون بين 1024 و65535.");
        if (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Length < 32) errors.Add("مفتاح الربط غير صالح.");
        return errors;
    }

    public string BuildConnectionString() => new SqlConnectionStringBuilder
    {
        DataSource = SqlServer.Trim(),
        InitialCatalog = Database.Trim(),
        UserID = Username.Trim(),
        Password = Password,
        IntegratedSecurity = false,
        Encrypt = false,
        TrustServerCertificate = true,
        ConnectTimeout = 8,
        ApplicationName = "Almutamakkin Barcode Bridge"
    }.ConnectionString;
}

public static class ApiKeyGenerator
{
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
