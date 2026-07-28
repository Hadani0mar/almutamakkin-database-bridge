using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class BridgeHealthHandler : ICommandHandler
{
    private readonly AppSettings _settings;
    private readonly IActiveRequestTracker _activeRequestTracker;

    public BridgeHealthHandler(AppSettings settings, IActiveRequestTracker activeRequestTracker)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _activeRequestTracker = activeRequestTracker ?? throw new ArgumentNullException(nameof(activeRequestTracker));
    }

    public string MessageType => MessageTypes.BridgeHealth;

    public Task<BridgeResponse> HandleAsync(
        BridgeCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            status = "online",
            tunnelId = _settings.TunnelId,
            transportMode = _settings.TransportMode.ToString(),
            activeQueries = _activeRequestTracker.ActiveCount,
            protocolVersion = BridgeLimits.SupportedProtocolVersion,
            checkedAtUtc = DateTime.UtcNow,
        };

        return Task.FromResult(BridgeResponseBuilder.Success(command, payload));
    }
}
