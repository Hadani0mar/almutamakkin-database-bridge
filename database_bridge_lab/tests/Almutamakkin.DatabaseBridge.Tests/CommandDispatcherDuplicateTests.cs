using System.Text.Json;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class CommandDispatcherDuplicateTests
{
    [Fact]
    public async Task DispatchAsync_SameRequestId_ReturnsCachedResponse()
    {
        var settings = new AppSettings { TunnelId = "LAB-TNL-001" };
        var validator = new RequestValidator(settings);
        var store = new InMemoryProcessedRequestStore();
        var logger = new TestBridgeLogger();
        var tracker = new ActiveRequestTracker();

        ICommandHandler healthHandler = new BridgeHealthHandler(settings, tracker);
        var dispatcher = new CommandDispatcher(validator, store, logger, settings, [healthHandler]);

        var command = new BridgeCommand
        {
            ProtocolVersion = BridgeLimits.SupportedProtocolVersion,
            MessageType = MessageTypes.BridgeHealth,
            RequestId = "REQ-DUP-001",
            TunnelId = settings.TunnelId,
            SentAtUtc = DateTime.UtcNow,
            Payload = JsonDocument.Parse("{}").RootElement.Clone(),
        };

        var first = await dispatcher.DispatchAsync(command, CancellationToken.None);
        var second = await dispatcher.DispatchAsync(command, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.RespondedAtUtc, second.RespondedAtUtc);
        Assert.Equal(first.RequestId, second.RequestId);
    }
}
