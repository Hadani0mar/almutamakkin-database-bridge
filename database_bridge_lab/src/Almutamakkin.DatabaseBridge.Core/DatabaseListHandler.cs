using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class DatabaseListHandler : ICommandHandler
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly ILiveDatabaseProfileResolver _profileResolver;
    private readonly ISqlServerDiscovery _discovery;
    private readonly ISecretProtector _secretProtector;
    private readonly IBridgeLogger _logger;

    public DatabaseListHandler(
        IDatabaseProfileStore profileStore,
        ILiveDatabaseProfileResolver profileResolver,
        ISqlServerDiscovery discovery,
        ISecretProtector secretProtector,
        IBridgeLogger logger)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _secretProtector = secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string MessageType => MessageTypes.DatabaseList;

    public async Task<BridgeResponse> HandleAsync(
        BridgeCommand command,
        CancellationToken cancellationToken)
    {
        var payload = BridgeJson.DeserializeDatabaseListPayload(command.Payload);
        if (payload is null || string.IsNullOrWhiteSpace(payload.DatabaseProfile))
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.InvalidMessage,
                "تعذر قراءة حمولة database.list.");
        }

        _profileStore.Reload();
        var profile = _profileResolver.Resolve(payload.DatabaseProfile);
        if (profile is null)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseProfileNotFound,
                "لا يوجد ملف اتصال مفعّل يطابق القاعدة المطلوبة. تحقق من databaseProfile ثم أعد المحاولة.");
        }

        try
        {
            var plainPassword = ResolvePlainPassword(profile);
            var databases = await _discovery.ListDatabasesAsync(
                profile.ServerName,
                profile.AuthenticationMode,
                profile.UserName,
                plainPassword,
                profile.TrustServerCertificate,
                profile.EncryptConnection,
                cancellationToken).ConfigureAwait(false);

            var responsePayload = new
            {
                databaseProfile = payload.DatabaseProfile,
                resolvedProfile = profile.ProfileName,
                resolvedDatabase = profile.DatabaseName,
                system = _profileResolver.GetSystem(profile),
                databases = databases.Select(database => new
                {
                    name = database.Name,
                    compatibilityHint = database.CompatibilityHint,
                }).ToList(),
            };

            return BridgeResponseBuilder.Success(command, responsePayload);
        }
        catch (Exception ex)
        {
            _logger.Error("Database list failed.", ex);
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseConnectionFailed,
                "تعذر جلب قائمة قواعد البيانات من السيرفر.",
                retryable: true);
        }
    }

    private string? ResolvePlainPassword(DatabaseProfile profile)
    {
        if (profile.AuthenticationMode == SqlAuthenticationMode.WindowsAuthentication)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(profile.EncryptedPassword))
        {
            throw new InvalidOperationException("SQL authentication requires an encrypted password.");
        }

        return _secretProtector.Unprotect(profile.EncryptedPassword);
    }
}
