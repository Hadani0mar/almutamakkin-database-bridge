using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// Phase 0/1 change-stream foundation. Same local-cursor read as
/// changes.probe, plus the watermark blob. Cloud tickets are published by
/// DomainWatchService via bridge-change-publish; the phone reads them with
/// Supabase RPC. This handler still returns local cursor status only —
/// a true delta pull over the tunnel is a later phase.
/// </summary>
public sealed class ChangesPullHandler : ICommandHandler
{
    private const string CloudPublishNotReadyMessage =
        "لم يُفعَّل نشر تذاكر التغيير السحابي بعد؛ هذه حالة المؤشر المحلي فقط.";

    private readonly AppSettings _settings;
    private readonly IChangeCursorStore _cursorStore;

    public ChangesPullHandler(AppSettings settings, IChangeCursorStore cursorStore)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cursorStore = cursorStore ?? throw new ArgumentNullException(nameof(cursorStore));
    }

    public string MessageType => MessageTypes.ChangesPull;

    public Task<BridgeResponse> HandleAsync(
        BridgeCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = BridgeJson.DeserializeChangesPullPayload(command.Payload);
        var requested = payload?.Domains is { Count: > 0 }
            ? payload.Domains
            : ChangeDomainCatalog.Domains
                .Select(descriptor => new ChangeDomainKey
                {
                    System = descriptor.System,
                    Domain = descriptor.Domain,
                })
                .ToList();

        var results = requested.Select(key => BuildDomainCursor(key)).ToList();

        var responsePayload = new
        {
            watchEnabled = _settings.EnableChangeStreamWatch,
            cloudPublishReady = false,
            message = CloudPublishNotReadyMessage,
            domains = results,
        };

        return Task.FromResult(BridgeResponseBuilder.Success(command, responsePayload));
    }

    private object BuildDomainCursor(ChangeDomainKey key)
    {
        var descriptor = ChangeDomainCatalog.Find(key.System, key.Domain);
        if (descriptor is null)
        {
            return new
            {
                system = key.System,
                domain = key.Domain,
                enabled = false,
                revision = 0L,
                watermarkJson = (string?)null,
                message = "نطاق مراقبة غير معروف.",
            };
        }

        var enabled = _settings.EnableChangeStreamWatch && descriptor.IsEnabled(_settings);
        var record = _cursorStore.Get(descriptor.System, descriptor.Domain);

        return new
        {
            system = descriptor.System,
            domain = descriptor.Domain,
            displayName = descriptor.DisplayName,
            enabled,
            revision = record?.Revision ?? 0L,
            watermarkJson = record?.WatermarkJson,
            lastCheckedUtc = record?.LastCheckedUtc,
            lastChangedUtc = record?.LastChangedUtc,
            message = enabled
                ? CloudPublishNotReadyMessage
                : "مراقبة الدلتا متوقفة لهذا النطاق.",
        };
    }
}
