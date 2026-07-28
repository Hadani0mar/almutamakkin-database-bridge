using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;
using Almutamakkin.DatabaseBridge.Protocol;
using Microsoft.Data.SqlClient;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Almutamakkin.DatabaseBridge.Infrastructure;

public sealed class SqlProductPhotoService : IProductPhotoService
{
    private const int MaxSourceBytes = 12 * 1024 * 1024;

    private readonly ISecretProtector _secretProtector;
    private readonly IConnectionStringBuilder _connectionStringBuilder;

    public SqlProductPhotoService(
        ISecretProtector secretProtector,
        IConnectionStringBuilder connectionStringBuilder)
    {
        _secretProtector = secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));
        _connectionStringBuilder = connectionStringBuilder
            ?? throw new ArgumentNullException(nameof(connectionStringBuilder));
    }

    public async Task<ProductPhotoLoadResult> LoadAsync(
        ProductPhotoLoadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);

        byte[]? sourceBytes;
        try
        {
            sourceBytes = await ReadPhotoBytesAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            return ProductPhotoLoadResult.Fail(
                request.ProductId,
                $"فشل قراءة صورة الصنف من قاعدة البيانات ({ex.Number}).");
        }

        if (sourceBytes is null || sourceBytes.Length == 0)
        {
            return ProductPhotoLoadResult.Missing(request.ProductId);
        }

        try
        {
            using var image = Image.Load(sourceBytes);
            var maxEdge = Math.Clamp(request.MaxEdgePx, 64, 1024);
            if (image.Width > maxEdge || image.Height > maxEdge)
            {
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxEdge, maxEdge),
                }));
            }

            var quality = Math.Clamp(request.JpegQuality, 40, 90);
            await using var output = new MemoryStream();
            await image.SaveAsJpegAsync(
                    output,
                    new JpegEncoder { Quality = quality },
                    cancellationToken)
                .ConfigureAwait(false);

            var jpegBytes = output.ToArray();
            return new ProductPhotoLoadResult
            {
                Found = true,
                ProductId = request.ProductId,
                MimeType = "image/jpeg",
                Encoding = "base64",
                Width = image.Width,
                Height = image.Height,
                Bytes = jpegBytes.Length,
                SourceBytes = sourceBytes.Length,
                DataBase64 = Convert.ToBase64String(jpegBytes),
            };
        }
        catch (UnknownImageFormatException)
        {
            return ProductPhotoLoadResult.Fail(
                request.ProductId,
                "صيغة صورة الصنف غير مدعومة.");
        }
        catch (Exception)
        {
            return ProductPhotoLoadResult.Fail(
                request.ProductId,
                "تعذر ضغط صورة الصنف.");
        }
    }

    public async Task<ProductPhotoUpsertResult> UpsertAsync(
        ProductPhotoUpsertRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.SourceImageBytes);

        if (request.ProductId <= 0)
        {
            return ProductPhotoUpsertResult.Fail(
                request.ProductId,
                ErrorCodes.InvalidMessage,
                "معرّف الصنف مطلوب ويجب أن يكون أكبر من صفر.");
        }

        if (request.SourceImageBytes.Length == 0)
        {
            return ProductPhotoUpsertResult.Fail(
                request.ProductId,
                ErrorCodes.InvalidMessage,
                "بيانات الصورة فارغة.");
        }

        if (request.SourceImageBytes.Length > MaxSourceBytes)
        {
            return ProductPhotoUpsertResult.Fail(
                request.ProductId,
                ErrorCodes.ResultTooLarge,
                "حجم الصورة المصدر أكبر من الحد المسموح.");
        }

        byte[] gifBytes;
        int width;
        int height;
        try
        {
            (gifBytes, width, height) = await ProductPhotoGifEncoder
                .EncodeAsync(request.SourceImageBytes, request.MaxEdgePx, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (UnknownImageFormatException)
        {
            return ProductPhotoUpsertResult.Fail(
                request.ProductId,
                ErrorCodes.ProductPhotoFailed,
                "صيغة الصورة المصدر غير مدعومة.");
        }
        catch (Exception ex)
        {
            return ProductPhotoUpsertResult.Fail(
                request.ProductId,
                ErrorCodes.ProductPhotoFailed,
                $"تعذر تحويل الصورة إلى GIF89a: {ex.Message}");
        }

        try
        {
            await WritePhotoBytesAsync(request, gifBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            return ProductPhotoUpsertResult.Fail(
                request.ProductId,
                ErrorCodes.SqlExecutionFailed,
                $"فشل حفظ صورة الصنف ({ex.Number}).");
        }

        return new ProductPhotoUpsertResult
        {
            Success = true,
            ProductId = request.ProductId,
            MimeType = "image/gif",
            Width = width,
            Height = height,
            Bytes = gifBytes.Length,
            SourceBytes = request.SourceImageBytes.Length,
            IsGif89a = ProductPhotoGifEncoder.StartsWithGif89a(gifBytes),
        };
    }

    private async Task<byte[]?> ReadPhotoBytesAsync(
        ProductPhotoLoadRequest request,
        CancellationToken cancellationToken)
    {
        var plainPassword = ResolvePlainPassword(request.Profile);
        var connectionString = _connectionStringBuilder.Build(request.Profile, plainPassword);
        var timeoutSeconds = request.Profile.CommandTimeoutSeconds > 0
            ? Math.Clamp(request.Profile.CommandTimeoutSeconds, 5, BridgeLimits.MaximumTimeoutSeconds)
            : BridgeLimits.DefaultTimeoutSeconds;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = timeoutSeconds;
        command.Parameters.Add(new SqlParameter("@productId", request.ProductId));

        if (IsMarketingProfile(request.Profile))
        {
            // App convention: ITEM_MODEL stores CAST(ITEM_ID AS varchar) because native
            // ITEM_MODEL is empty for nearly all Marketing rows.
            command.CommandText = """
                SELECT TOP (1) IMAGE
                FROM dbo.ITEM_IMAGES
                WHERE ITEM_MODEL = CONVERT(varchar(50), @productId)
                  AND DATALENGTH(IMAGE) > 0;
                """;
        }
        else
        {
            command.CommandText = """
                SELECT TOP (1) Photo
                FROM Inventory.Data_ProductPhotos
                WHERE ProductID_PK = @productId
                  AND DATALENGTH(Photo) > 0;
                """;
        }

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is byte[] bytes && bytes.Length > 0 ? bytes : null;
    }

    private async Task WritePhotoBytesAsync(
        ProductPhotoUpsertRequest request,
        byte[] gifBytes,
        CancellationToken cancellationToken)
    {
        var plainPassword = ResolvePlainPassword(request.Profile);
        var connectionString = _connectionStringBuilder.Build(request.Profile, plainPassword);
        var timeoutSeconds = request.Profile.CommandTimeoutSeconds > 0
            ? Math.Clamp(request.Profile.CommandTimeoutSeconds, 5, BridgeLimits.MaximumTimeoutSeconds)
            : BridgeLimits.DefaultTimeoutSeconds;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (IsMarketingProfile(request.Profile))
        {
            await WriteMarketingPhotoAsync(
                    connection,
                    timeoutSeconds,
                    request.ProductId,
                    gifBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandTimeout = timeoutSeconds;
        command.CommandText = """
            MERGE Inventory.Data_ProductPhotos AS target
            USING (SELECT @productId AS ProductID_PK) AS src
              ON target.ProductID_PK = src.ProductID_PK
            WHEN MATCHED THEN
              UPDATE SET Photo = @photo
            WHEN NOT MATCHED THEN
              INSERT (ProductID_PK, Photo) VALUES (@productId, @photo);
            """;
        command.Parameters.Add(new SqlParameter("@productId", request.ProductId));
        command.Parameters.Add(new SqlParameter("@photo", gifBytes)
        {
            SqlDbType = System.Data.SqlDbType.Image,
        });

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteMarketingPhotoAsync(
        SqlConnection connection,
        int timeoutSeconds,
        long productId,
        byte[] gifBytes,
        CancellationToken cancellationToken)
    {
        string? itemName = null;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.CommandTimeout = timeoutSeconds;
            lookup.CommandText = """
                SELECT TOP (1) ITEM_NAME
                FROM dbo.ITEMS
                WHERE ITEM_ID = @productId;
                """;
            lookup.Parameters.Add(new SqlParameter("@productId", productId));
            var nameValue = await lookup.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            itemName = nameValue?.ToString();
            if (string.IsNullOrWhiteSpace(itemName))
            {
                throw new InvalidOperationException(
                    $"ITEM_ID {productId} غير موجود في dbo.ITEMS.");
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandTimeout = timeoutSeconds;
        command.CommandText = """
            DECLARE @key varchar(50);
            SET @key = CONVERT(varchar(50), @productId);
            IF EXISTS (
                SELECT 1
                FROM dbo.ITEM_IMAGES WITH (UPDLOCK, HOLDLOCK)
                WHERE ITEM_MODEL = @key
            )
            BEGIN
                UPDATE dbo.ITEM_IMAGES
                SET IMAGE = @photo,
                    ITEM_NAME = @itemName
                WHERE ITEM_MODEL = @key;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.ITEM_IMAGES (ITEM_MODEL, ITEM_NAME, PRICE, IMAGE, QTY1, PRICE2)
                VALUES (@key, @itemName, NULL, @photo, NULL, NULL);
            END
            """;
        command.Parameters.Add(new SqlParameter("@productId", productId));
        command.Parameters.Add(new SqlParameter("@itemName", itemName));
        command.Parameters.Add(new SqlParameter("@photo", gifBytes)
        {
            SqlDbType = System.Data.SqlDbType.Image,
        });

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsMarketingProfile(DatabaseProfile profile) =>
        profile.ProfileName.Contains("Marketing", StringComparison.OrdinalIgnoreCase) ||
        (profile.DatabaseName?.Equals("Marketing", StringComparison.OrdinalIgnoreCase) ?? false);

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
