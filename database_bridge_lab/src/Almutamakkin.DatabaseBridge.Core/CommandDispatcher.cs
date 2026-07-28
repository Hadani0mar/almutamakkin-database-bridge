using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly IRequestValidator _validator;
    private readonly IProcessedRequestStore _processedRequestStore;
    private readonly IBridgeLogger _logger;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly IReadOnlyDictionary<string, ICommandHandler> _handlers;

    public CommandDispatcher(
        IRequestValidator validator,
        IProcessedRequestStore processedRequestStore,
        IBridgeLogger logger,
        AppSettings settings,
        IEnumerable<ICommandHandler> handlers)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _processedRequestStore = processedRequestStore ?? throw new ArgumentNullException(nameof(processedRequestStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        var handlerList = handlers?.ToList() ?? throw new ArgumentNullException(nameof(handlers));
        _handlers = handlerList.ToDictionary(
            handler => handler.MessageType,
            handler => handler,
            StringComparer.OrdinalIgnoreCase);

        var maxConcurrent = Math.Max(1, _settings.MaxConcurrentQueries);
        _concurrencySemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public async Task<BridgeResponse> DispatchAsync(
        BridgeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        _processedRequestStore.CleanupExpired();

        if (_processedRequestStore.TryGetResponse(command.RequestId, out var previousResponse) &&
            previousResponse is not null)
        {
            _logger.Info($"Returning cached response for duplicate request {command.RequestId}.");
            return previousResponse;
        }

        var validation = _validator.ValidateCommand(command);
        if (!validation.IsValid)
        {
            return BridgeResponseBuilder.FromValidation(command, validation);
        }

        if (!_handlers.TryGetValue(command.MessageType, out var handler))
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.UnsupportedCommand,
                $"نوع الرسالة '{command.MessageType}' غير مدعوم.");
        }

        var requiresConcurrencySlot = RequiresConcurrencySlot(command.MessageType);
        if (requiresConcurrencySlot &&
            !await _concurrencySemaphore.WaitAsync(0, cancellationToken))
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.BridgeBusy,
                "الجسر ينفذ الحد الأقصى من الاستعلامات حالياً.",
                retryable: true);
        }

        try
        {
            var response = await handler.HandleAsync(command, cancellationToken);
            _processedRequestStore.Store(command.RequestId, response);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error($"Unhandled dispatcher error for request {command.RequestId}.", ex);
            var errorResponse = BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.InternalError,
                "حدث خطأ داخلي في الجسر.");

            _processedRequestStore.Store(command.RequestId, errorResponse);
            return errorResponse;
        }
        finally
        {
            if (requiresConcurrencySlot)
            {
                _concurrencySemaphore.Release();
            }
        }
    }

    private static bool RequiresConcurrencySlot(string messageType) =>
        string.Equals(messageType, MessageTypes.SqlExecute, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(messageType, MessageTypes.QueryPackageExecute, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(messageType, MessageTypes.MarketingProductMovement, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(messageType, MessageTypes.InfinityProductMovement, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(messageType, MessageTypes.DatabaseTest, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(messageType, MessageTypes.ProductPhoto, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(messageType, MessageTypes.ProductPhotoUpsert, StringComparison.OrdinalIgnoreCase);
}
