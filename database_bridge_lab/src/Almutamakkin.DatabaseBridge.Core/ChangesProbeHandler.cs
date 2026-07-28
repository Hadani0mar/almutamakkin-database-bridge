using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// Phase 0/1 change-stream foundation. Cheap: never touches SQL Server,
/// only reads the local <see cref="IChangeCursorStore"/> that
/// DomainWatchService maintains. Lets the phone ask "did anything change
/// since revision N?" without pulling a full snapshot.
/// </summary>
public sealed class ChangesProbeHandler : ICommandHandler
{
    private readonly AppSettings _settings;
    private readonly IChangeCursorStore _cursorStore;

    public ChangesProbeHandler(AppSettings settings, IChangeCursorStore cursorStore)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cursorStore = cursorStore ?? throw new ArgumentNullException(nameof(cursorStore));
    }

    public string MessageType => MessageTypes.ChangesProbe;

    public Task<BridgeResponse> HandleAsync(
        BridgeCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = BridgeJson.DeserializeChangesProbePayload(command.Payload);
        var requested = payload?.Domains is { Count: > 0 }
            ? payload.Domains
            : ChangeDomainCatalog.Domains
                .Select(descriptor => new ChangeDomainKey
                {
                    System = descriptor.System,
                    Domain = descriptor.Domain,
                })
                .ToList();

        var results = requested.Select(key => BuildDomainStatus(key)).ToList();

        var responsePayload = new
        {
            watchEnabled = _settings.EnableChangeStreamWatch,
            domains = results,
        };

        return Task.FromResult(BridgeResponseBuilder.Success(command, responsePayload));
    }

    private object BuildDomainStatus(ChangeDomainKey key)
    {
        var descriptor = ChangeDomainCatalog.Find(key.System, key.Domain);
        if (descriptor is null)
        {
            return new
            {
                system = key.System,
                domain = key.Domain,
                enabled = false,
                currentRevision = 0L,
                changed = false,
                message = "نطاق مراقبة غير معروف.",
            };
        }

        var enabled = _settings.EnableChangeStreamWatch && descriptor.IsEnabled(_settings);
        var record = _cursorStore.Get(descriptor.System, descriptor.Domain);
        var currentRevision = record?.Revision ?? 0L;
        var changed = key.KnownRevision.HasValue
            ? currentRevision > key.KnownRevision.Value
            : currentRevision > 0;

        return new
        {
            system = descriptor.System,
            domain = descriptor.Domain,
            displayName = descriptor.DisplayName,
            enabled,
            currentRevision,
            changed,
            lastCheckedUtc = record?.LastCheckedUtc,
            lastChangedUtc = record?.LastChangedUtc,
            message = enabled
                ? (changed ? "يوجد تغيير جديد." : "لا يوجد تغيير جديد.")
                : "مراقبة الدلتا متوقفة لهذا النطاق.",
        };
    }
}
