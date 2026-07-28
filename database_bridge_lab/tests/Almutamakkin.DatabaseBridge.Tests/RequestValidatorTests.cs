using System.Text.Json;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class RequestValidatorTests
{
    private readonly AppSettings _settings = new() { TunnelId = "LAB-TNL-001" };
    private readonly RequestValidator _validator;

    public RequestValidatorTests()
    {
        _validator = new RequestValidator(_settings);
    }

    [Fact]
    public void ValidateSqlExecutePayload_EmptySql_Fails()
    {
        var payload = new SqlExecutePayload
        {
            DatabaseProfile = "BridgeLab",
            Sql = string.Empty,
        };

        var result = _validator.ValidateSqlExecutePayload(payload);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.SqlEmpty, result.ErrorCode);
    }

    [Fact]
    public void ValidateCommand_UnsupportedProtocol_Fails()
    {
        var command = CreateCommand("9.9", MessageTypes.BridgeHealth, "{}");

        var result = _validator.ValidateCommand(command);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.UnsupportedProtocol, result.ErrorCode);
    }

    [Fact]
    public void ValidateCommand_ExpiredRequest_Fails()
    {
        var command = CreateCommand(
            BridgeLimits.SupportedProtocolVersion,
            MessageTypes.BridgeHealth,
            "{}",
            sentAtUtc: DateTime.UtcNow.AddMinutes(-10));

        var result = _validator.ValidateCommand(command);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.RequestExpired, result.ErrorCode);
    }

    [Fact]
    public void ValidateCommand_TunnelMismatch_Fails()
    {
        var command = CreateCommand(
            BridgeLimits.SupportedProtocolVersion,
            MessageTypes.BridgeHealth,
            "{}",
            tunnelId: "OTHER-TUNNEL");

        var result = _validator.ValidateCommand(command);

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.InvalidTunnelId, result.ErrorCode);
    }

    [Fact]
    public void ValidateSqlExecutePayload_BadParameterType_Fails()
    {
        var payload = new SqlExecutePayload
        {
            DatabaseProfile = "BridgeLab",
            Sql = "SELECT @p",
            Parameters = new Dictionary<string, SqlParameterValue>
            {
                ["p"] = new SqlParameterValue { Type = "money", Value = 1 },
            },
        };

        var result = _validator.ValidateSqlExecutePayload(payload);

        Assert.False(result.IsValid);
    }

    private BridgeCommand CreateCommand(
        string protocolVersion,
        string messageType,
        string payloadJson,
        string? tunnelId = null,
        DateTime? sentAtUtc = null) =>
        new()
        {
            ProtocolVersion = protocolVersion,
            MessageType = messageType,
            RequestId = "REQ-TEST-001",
            TunnelId = tunnelId ?? _settings.TunnelId,
            SentAtUtc = sentAtUtc ?? DateTime.UtcNow,
            Payload = JsonDocument.Parse(payloadJson).RootElement.Clone(),
        };
}
