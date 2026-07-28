namespace Almutamakkin.DatabaseBridge.App;

internal static class SampleCommands
{
    public const string TunnelId = "LAB-TNL-001";
    public const string ProtocolVersion = "1.0";

    public static IReadOnlyList<(string Name, string Json)> All { get; } =
    [
        ("Health Check", HealthCheck),
        ("Database Test", DatabaseTest),
        ("SELECT TOP 10", SelectTop10),
        ("Parameterized SELECT", ParameterizedSelect),
        ("UPDATE Test", UpdateTest),
        ("Invalid Permission", InvalidPermission),
        ("Timeout Test", TimeoutTest),
        ("Large Result Test", LargeResultTest),
    ];

    public static string HealthCheck =>
        $$"""
        {
          "protocolVersion": "{{ProtocolVersion}}",
          "messageType": "bridge.health",
          "requestId": "{{NewRequestId()}}",
          "tunnelId": "{{TunnelId}}",
          "sentAtUtc": "{{UtcNow()}}",
          "payload": {}
        }
        """;

    public static string DatabaseTest =>
        $$"""
        {
          "protocolVersion": "{{ProtocolVersion}}",
          "messageType": "database.test",
          "requestId": "{{NewRequestId()}}",
          "tunnelId": "{{TunnelId}}",
          "sentAtUtc": "{{UtcNow()}}",
          "payload": {
            "databaseProfile": "BridgeLab"
          }
        }
        """;

    public static string SelectTop10 =>
        $$"""
        {
          "protocolVersion": "{{ProtocolVersion}}",
          "messageType": "sql.execute",
          "requestId": "{{NewRequestId()}}",
          "tunnelId": "{{TunnelId}}",
          "sentAtUtc": "{{UtcNow()}}",
          "payload": {
            "databaseProfile": "BridgeLab",
            "sql": "SELECT TOP 10 Id, ItemName, Quantity, IsActive, CreatedAt FROM dbo.BridgeTestItems ORDER BY Id",
            "parameters": {},
            "timeoutSeconds": 30,
            "maxRows": 1000
          }
        }
        """;

    public static string ParameterizedSelect =>
        $$"""
        {
          "protocolVersion": "{{ProtocolVersion}}",
          "messageType": "sql.execute",
          "requestId": "{{NewRequestId()}}",
          "tunnelId": "{{TunnelId}}",
          "sentAtUtc": "{{UtcNow()}}",
          "payload": {
            "databaseProfile": "BridgeLab",
            "sql": "SELECT Id, ItemName, Quantity FROM dbo.BridgeTestItems WHERE IsActive = @active AND Quantity >= @minQty ORDER BY Id",
            "parameters": {
              "active": { "type": "bool", "value": true },
              "minQty": { "type": "int", "value": 1 }
            },
            "timeoutSeconds": 30,
            "maxRows": 100
          }
        }
        """;

    public static string UpdateTest =>
        $$"""
        {
          "protocolVersion": "{{ProtocolVersion}}",
          "messageType": "sql.execute",
          "requestId": "{{NewRequestId()}}",
          "tunnelId": "{{TunnelId}}",
          "sentAtUtc": "{{UtcNow()}}",
          "payload": {
            "databaseProfile": "BridgeLab",
            "sql": "UPDATE dbo.BridgeTestItems SET Quantity = Quantity + 1 WHERE Id = (SELECT MIN(Id) FROM dbo.BridgeTestItems WHERE IsActive = 1)",
            "parameters": {},
            "timeoutSeconds": 30,
            "maxRows": 1000
          }
        }
        """;

    public static string InvalidPermission =>
        $$"""
        {
          "protocolVersion": "{{ProtocolVersion}}",
          "messageType": "sql.execute",
          "requestId": "{{NewRequestId()}}",
          "tunnelId": "{{TunnelId}}",
          "sentAtUtc": "{{UtcNow()}}",
          "payload": {
            "databaseProfile": "BridgeLab",
            "sql": "CREATE TABLE dbo.ForbiddenLabTable (Id INT PRIMARY KEY)",
            "parameters": {},
            "timeoutSeconds": 30,
            "maxRows": 1000
          }
        }
        """;

    public static string TimeoutTest =>
        $$"""
        {
          "protocolVersion": "{{ProtocolVersion}}",
          "messageType": "sql.execute",
          "requestId": "{{NewRequestId()}}",
          "tunnelId": "{{TunnelId}}",
          "sentAtUtc": "{{UtcNow()}}",
          "payload": {
            "databaseProfile": "BridgeLab",
            "sql": "WAITFOR DELAY '00:00:45'; SELECT 1 AS Done",
            "parameters": {},
            "timeoutSeconds": 2,
            "maxRows": 10
          }
        }
        """;

    public static string LargeResultTest =>
        $$"""
        {
          "protocolVersion": "{{ProtocolVersion}}",
          "messageType": "sql.execute",
          "requestId": "{{NewRequestId()}}",
          "tunnelId": "{{TunnelId}}",
          "sentAtUtc": "{{UtcNow()}}",
          "payload": {
            "databaseProfile": "BridgeLab",
            "sql": "SELECT TOP 5000 Id, ItemName FROM dbo.BridgeTestItems ORDER BY Id",
            "parameters": {},
            "timeoutSeconds": 30,
            "maxRows": 5
          }
        }
        """;

    private static string UtcNow() => DateTime.UtcNow.ToString("O");

    private static string NewRequestId() =>
        $"REQ-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Random.Shared.Next(1000, 9999)}";
}
