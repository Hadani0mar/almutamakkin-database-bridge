using System.Text.Json;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class ProductPhotoUpsertHandler : ICommandHandler
{
    private const int DefaultMaxEdgePx = 640;
    private const int MinMaxEdgePx = 64;
    private const int MaxMaxEdgePx = 1024;
    private const int MaxBase64Chars = 16 * 1024 * 1024;

    private readonly AppSettings _settings;
    private readonly IDatabaseProfileStore _profileStore;
    private readonly ILiveDatabaseProfileResolver _profileResolver;
    private readonly IProductPhotoService _photoService;
    private readonly IBridgeLogger _logger;

    public ProductPhotoUpsertHandler(
        AppSettings settings,
        IDatabaseProfileStore profileStore,
        ILiveDatabaseProfileResolver profileResolver,
        IProductPhotoService photoService,
        IBridgeLogger logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string MessageType => MessageTypes.ProductPhotoUpsert;

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

        var imageBase64 = ReadString(command.Payload, "imageBase64")
            ?? ReadString(command.Payload, "data");
        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.InvalidMessage,
                "بيانات الصورة (imageBase64) مطلوبة.");
        }

        if (imageBase64.Length > MaxBase64Chars)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.ResultTooLarge,
                "حجم بيانات الصورة أكبر من الحد المسموح.");
        }

        byte[] sourceBytes;
        try
        {
            sourceBytes = Convert.FromBase64String(imageBase64.Trim());
        }
        catch (FormatException)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.InvalidMessage,
                "بيانات الصورة ليست Base64 صالحاً.");
        }

        var system = ReadString(command.Payload, "system")?.Trim().ToLowerInvariant();
        var profileName = ReadString(command.Payload, "databaseProfile");
        var wantsMarketing = IsMarketingSystem(system);
        var wantsInfinity = IsInfinitySystem(system);
        if (!string.IsNullOrWhiteSpace(system) && !wantsMarketing && !wantsInfinity)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.SqlPermissionDenied,
                "كتابة صور المنتجات مسموحة لأبوغريس أو إنفينيتي فقط.");
        }

        var defaultProfile = wantsMarketing ? "Marketing" : "InfinityRetailDB";
        _profileStore.Reload();
        var profile = _profileResolver.Resolve(
            string.IsNullOrWhiteSpace(profileName) ? defaultProfile : profileName);
        if (profile is null)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseProfileNotFound,
                "لا توجد قاعدة بيانات جاهزة لحفظ صورة الصنف.");
        }

        if (!profile.IsEnabled)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.DatabaseProfileDisabled,
                $"الملف '{profile.ProfileName}' غير مفعّل.");
        }

        var profileIsMarketing = LooksLikeMarketing(profile);
        var profileIsInfinity = LooksLikeInfinity(profile);
        if (wantsMarketing && !profileIsMarketing)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.SqlPermissionDenied,
                "ملف القاعدة لا يطابق نظام أبوغريس.");
        }

        if (wantsInfinity && !profileIsInfinity)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.SqlPermissionDenied,
                "ملف القاعدة لا يطابق نظام إنفينيتي.");
        }

        if (!wantsMarketing && !wantsInfinity)
        {
            // Infer from profile when system omitted.
            profileIsMarketing = LooksLikeMarketing(profile);
            profileIsInfinity = LooksLikeInfinity(profile);
        }

        if (profileIsMarketing)
        {
            if (!_settings.EnableMarketingProductPhotoWrite)
            {
                return BridgeResponseBuilder.Failure(
                    command,
                    ErrorCodes.ProductPhotoWriteDisabled,
                    "كتابة صور أصناف أبوغريس غير مفعّلة على الجسر.");
            }
        }
        else if (profileIsInfinity)
        {
            if (!_settings.EnableInfinityProductPhotoWrite)
            {
                return BridgeResponseBuilder.Failure(
                    command,
                    ErrorCodes.ProductPhotoWriteDisabled,
                    "كتابة صور أصناف إنفينيتي غير مفعّلة على الجسر.");
            }
        }
        else
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.SqlPermissionDenied,
                "كتابة صور المنتجات مسموحة لملف أبوغريس أو إنفينيتي فقط.");
        }

        var maxEdgePx = Clamp(
            ReadInt(command.Payload, "maxEdgePx") ?? DefaultMaxEdgePx,
            MinMaxEdgePx,
            MaxMaxEdgePx);

        try
        {
            var result = await _photoService.UpsertAsync(
                    new ProductPhotoUpsertRequest
                    {
                        Profile = profile,
                        ProductId = productId.Value,
                        SourceImageBytes = sourceBytes,
                        MaxEdgePx = maxEdgePx,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                return BridgeResponseBuilder.Failure(
                    command,
                    result.ErrorCode ?? ErrorCodes.ProductPhotoFailed,
                    result.ErrorMessage ?? "تعذر حفظ صورة الصنف.");
            }

            _logger.Info(
                $"product.photo.upsert saved {result.ProductId}: {result.Bytes} bytes " +
                $"GIF89a={result.IsGif89a} ({result.Width}x{result.Height}).");

            return BridgeResponseBuilder.Success(
                command,
                new
                {
                    productId = result.ProductId,
                    mimeType = result.MimeType,
                    width = result.Width,
                    height = result.Height,
                    bytes = result.Bytes,
                    sourceBytes = result.SourceBytes,
                    isGif89a = result.IsGif89a,
                });
        }
        catch (OperationCanceledException)
        {
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.SqlTimeout,
                "انتهت مهلة حفظ صورة الصنف.",
                retryable: true);
        }
        catch (Exception ex)
        {
            _logger.Error($"product.photo.upsert failed for {productId}.", ex);
            return BridgeResponseBuilder.Failure(
                command,
                ErrorCodes.ProductPhotoFailed,
                "تعذر حفظ صورة الصنف.");
        }
    }

    private static bool IsMarketingSystem(string? system) =>
        string.Equals(system, "marketing", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(system, "aboghris", StringComparison.OrdinalIgnoreCase);

    private static bool IsInfinitySystem(string? system) =>
        string.Equals(system, "infinity", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(system, "infinityretail", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(system, "infinityretaildb", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeMarketing(DatabaseProfile profile) =>
        profile.ProfileName.Contains("Marketing", StringComparison.OrdinalIgnoreCase) ||
        (profile.DatabaseName?.Equals("Marketing", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool LooksLikeInfinity(DatabaseProfile profile) =>
        profile.ProfileName.Contains("Infinity", StringComparison.OrdinalIgnoreCase) ||
        (profile.DatabaseName?.Contains("Infinity", StringComparison.OrdinalIgnoreCase) ?? false);

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
