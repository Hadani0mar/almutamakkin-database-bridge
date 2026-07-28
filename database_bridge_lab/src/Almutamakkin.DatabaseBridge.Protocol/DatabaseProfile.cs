using System.Text.Json.Serialization;

namespace Almutamakkin.DatabaseBridge.Protocol;

public enum SqlAuthenticationMode
{
    WindowsAuthentication,
    SqlAuthentication,
}

public enum SqlPermissionLevel
{
    ReadOnly,
    ReadWrite,
    FullAccess,
    Custom,
}

/// <summary>
/// The route used to reach the SQL Server. This is deliberately persisted on
/// the profile so a network profile can never be treated as a local discovery
/// result just because it happens to use the same database name.
/// </summary>
public enum DatabaseConnectionKind
{
    Local,
    Network,
}

public sealed class CustomPermissionOptions
{
    [JsonPropertyName("allowRead")]
    public bool AllowRead { get; set; } = true;

    [JsonPropertyName("allowInsert")]
    public bool AllowInsert { get; set; }

    [JsonPropertyName("allowUpdate")]
    public bool AllowUpdate { get; set; }

    [JsonPropertyName("allowDelete")]
    public bool AllowDelete { get; set; }

    [JsonPropertyName("allowExecuteProcedure")]
    public bool AllowExecuteProcedure { get; set; }

    [JsonPropertyName("allowSchemaChanges")]
    public bool AllowSchemaChanges { get; set; }

    [JsonPropertyName("allowAdministrativeCommands")]
    public bool AllowAdministrativeCommands { get; set; }
}

public sealed class DatabaseProfile
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("profileName")]
    public string ProfileName { get; set; } = string.Empty;

    [JsonPropertyName("serverName")]
    public string ServerName { get; set; } = string.Empty;

    [JsonPropertyName("databaseName")]
    public string DatabaseName { get; set; } = string.Empty;

    [JsonPropertyName("connectionKind")]
    public DatabaseConnectionKind ConnectionKind { get; set; } = DatabaseConnectionKind.Local;

    [JsonPropertyName("authenticationMode")]
    public SqlAuthenticationMode AuthenticationMode { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("encryptedPassword")]
    public string? EncryptedPassword { get; set; }

    [JsonPropertyName("trustServerCertificate")]
    public bool TrustServerCertificate { get; set; } = true;

    [JsonPropertyName("encryptConnection")]
    public bool EncryptConnection { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("permissionLevel")]
    public SqlPermissionLevel PermissionLevel { get; set; } = SqlPermissionLevel.ReadOnly;

    [JsonPropertyName("customPermissions")]
    public CustomPermissionOptions CustomPermissions { get; set; } = new();

    [JsonPropertyName("commandTimeoutSeconds")]
    public int CommandTimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("maximumRows")]
    public int MaximumRows { get; set; } = 1000;
}
