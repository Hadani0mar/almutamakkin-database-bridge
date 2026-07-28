using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class DatabaseTestHandler : ICommandHandler
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly IDatabaseConnectionTester _connectionTester;
    private readonly IBridgeLogger _logger;

    public DatabaseTestHandler(
        IDatabaseProfileStore profileStore,
        IDatabaseConnectionTester connectionTester,
        IBridgeLogger logger)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string MessageType => MessageTypes.DatabaseTest;

    public async Task<BridgeResponse> HandleAsync(
        BridgeCommand command,
        CancellationToken cancellationToken)
    {
        var payload = BridgeJson.DeserializeDatabaseTestPayload(command.Payload);
        if (payload is null)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.InvalidMessage,
                "تعذر قراءة حمولة database.test.");
        }

        _profileStore.Reload();
        var profile = _profileStore.GetByName(payload.DatabaseProfile);
        if (profile is null)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseProfileNotFound,
                $"ملف الاتصال '{payload.DatabaseProfile}' غير موجود.");
        }

        if (!profile.IsEnabled)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseProfileDisabled,
                $"ملف الاتصال '{payload.DatabaseProfile}' غير مفعّل.");
        }

        try
        {
            var result = await _connectionTester.TestAsync(profile, cancellationToken);
            if (!result.Success)
            {
                return BridgeResponseBuilder.Failure(
                    command,
                    ErrorCodes.DatabaseConnectionFailed,
                    result.Message,
                    result.Details,
                    retryable: true);
            }

            var responsePayload = new
            {
                databaseProfile = payload.DatabaseProfile,
                connected = true,
                message = result.Message,
                databaseName = result.DatabaseName,
                serverName = result.ServerName,
                loginName = result.LoginName,
                details = result.Details,
            };

            return BridgeResponseBuilder.Success(command, responsePayload);
        }
        catch (Exception ex)
        {
            _logger.Error("Database test failed.", ex);
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.InternalError,
                "حدث خطأ داخلي أثناء اختبار الاتصال.");
        }
    }
}
