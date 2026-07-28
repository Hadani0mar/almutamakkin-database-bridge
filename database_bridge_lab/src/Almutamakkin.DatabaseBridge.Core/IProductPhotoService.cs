using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public sealed class ProductPhotoLoadRequest
{
    public required DatabaseProfile Profile { get; init; }

    public long ProductId { get; init; }

    public int MaxEdgePx { get; init; } = 512;

    public int JpegQuality { get; init; } = 75;
}

public sealed class ProductPhotoLoadResult
{
    public bool Found { get; init; }

    public long ProductId { get; init; }

    public string MimeType { get; init; } = "image/jpeg";

    public string Encoding { get; init; } = "base64";

    public int Width { get; init; }

    public int Height { get; init; }

    public int Bytes { get; init; }

    public string? DataBase64 { get; init; }

    public int SourceBytes { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static ProductPhotoLoadResult Missing(long productId) => new()
    {
        Found = false,
        ProductId = productId,
        ErrorCode = ErrorCodes.ProductPhotoNotFound,
        ErrorMessage = "لا توجد صورة لهذا الصنف.",
    };

    public static ProductPhotoLoadResult Fail(long productId, string message) => new()
    {
        Found = false,
        ProductId = productId,
        ErrorCode = ErrorCodes.ProductPhotoFailed,
        ErrorMessage = message,
    };
}

public sealed class ProductPhotoUpsertRequest
{
    public required DatabaseProfile Profile { get; init; }

    public long ProductId { get; init; }

    public required byte[] SourceImageBytes { get; init; }

    public int MaxEdgePx { get; init; } = 640;
}

public sealed class ProductPhotoUpsertResult
{
    public bool Success { get; init; }

    public long ProductId { get; init; }

    public string MimeType { get; init; } = "image/gif";

    public int Width { get; init; }

    public int Height { get; init; }

    public int Bytes { get; init; }

    public int SourceBytes { get; init; }

    public bool IsGif89a { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static ProductPhotoUpsertResult Fail(long productId, string code, string message) => new()
    {
        Success = false,
        ProductId = productId,
        ErrorCode = code,
        ErrorMessage = message,
    };
}

public interface IProductPhotoService
{
    Task<ProductPhotoLoadResult> LoadAsync(
        ProductPhotoLoadRequest request,
        CancellationToken cancellationToken);

    Task<ProductPhotoUpsertResult> UpsertAsync(
        ProductPhotoUpsertRequest request,
        CancellationToken cancellationToken);
}
