using System.Text.Json;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class ProductPhotoHandler : ICommandHandler
{
    private const int DefaultMaxEdgePx = 512;
    private const int MinMaxEdgePx = 64;
    private const int MaxMaxEdgePx = 1024;
    private const int DefaultJpegQuality = 75;

    private readonly IDatabaseProfileStore _profileStore;
    private readonly ILiveDatabaseProfileResolver _profileResolver;
    private readonly IProductPhotoService _photoService;
    private readonly IBridgeLogger _logger;

    public ProductPhotoHandler(
        IDatabaseProfileStore profileStore,
        ILiveDatabaseProfileResolver profileResolver,
        IProductPhotoService photoService,
        IBridgeLogger logger)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string MessageType => MessageTypes.ProductPhoto;

    public async Task<BridgeResponse> HandleAsync(
        BridgeCommand command,
        CancellationToken cancellationToken)
    {
        var productId = ReadInt64(command.Payload, "productId");
        if (productId is null or <= 0)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.InvalidMessage,
                "معرّف الصنف مطلوب ويجب أن يكون أكبر من صفر.");
        }

        var maxEdgePx = Clamp(
            ReadInt(command.Payload, "maxEdgePx") ?? DefaultMaxEdgePx,
            MinMaxEdgePx,
            MaxMaxEdgePx);
        var jpegQuality = Clamp(
            ReadInt(command.Payload, "jpegQuality") ?? DefaultJpegQuality,
            40,
            90);

        _profileStore.Reload();
        var profileName = ReadString(command.Payload, "databaseProfile");
        var profile = _profileResolver.Resolve(profileName);
        if (profile is null)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseProfileNotFound,
                "لا توجد قاعدة بيانات جاهزة لصورة الصنف.");
        }

        if (!profile.IsEnabled)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseProfileDisabled,
                $"الملف '{profile.ProfileName}' غير مفعّل.");
        }

        try
        {
            var result = await _photoService.LoadAsync(
                new ProductPhotoLoadRequest
                {
                    Profile = profile,
                    ProductId = productId.Value,
                    MaxEdgePx = maxEdgePx,
                    JpegQuality = jpegQuality,
                },
                cancellationToken);

            if (!result.Found || string.IsNullOrWhiteSpace(result.DataBase64))
            {
                return BridgeResponseBuilder.Failure(
                    command,
                    result.ErrorCode ?? ErrorCodes.ProductPhotoNotFound,
                    result.ErrorMessage ?? "لا توجد صورة لهذا الصنف.");
            }

            _logger.Info(
                $"product.photo ready for {result.ProductId}: {result.Bytes} bytes " +
                $"({result.Width}x{result.Height}) from {result.SourceBytes} source bytes.");

            return BridgeResponseBuilder.Success(
                command,
                new
                {
                    productId = result.ProductId,
                    mimeType = result.MimeType,
                    encoding = result.Encoding,
                    width = result.Width,
                    height = result.Height,
                    bytes = result.Bytes,
                    sourceBytes = result.SourceBytes,
                    data = result.DataBase64,
                });
        }
        catch (OperationCanceledException)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.SqlTimeout,
                "انتهت مهلة جلب صورة الصنف.",
                retryable: true);
        }
        catch (Exception ex)
        {
            _logger.Error($"product.photo failed for {productId}.", ex);
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.ProductPhotoFailed,
                "تعذر تجهيز صورة الصنف.");
        }
    }

    private static string? ReadString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (!payload.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? ReadInt(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (!payload.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? ReadInt64(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        if (!payload.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;
}
