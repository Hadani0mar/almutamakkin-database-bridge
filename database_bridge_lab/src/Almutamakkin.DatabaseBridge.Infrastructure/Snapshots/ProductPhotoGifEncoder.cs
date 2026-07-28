using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

/// <summary>
/// Resizes and encodes product photos as GIF89a for InfinityRetailDB storage.
/// </summary>
public static class ProductPhotoGifEncoder
{
    public static readonly byte[] Gif89aHeader =
        [0x47, 0x49, 0x46, 0x38, 0x39, 0x61];

    public static bool StartsWithGif89a(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 6 &&
        bytes[0] == Gif89aHeader[0] &&
        bytes[1] == Gif89aHeader[1] &&
        bytes[2] == Gif89aHeader[2] &&
        bytes[3] == Gif89aHeader[3] &&
        bytes[4] == Gif89aHeader[4] &&
        bytes[5] == Gif89aHeader[5];

    public static async Task<(byte[] GifBytes, int Width, int Height)> EncodeAsync(
        byte[] sourceBytes,
        int maxEdgePx,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        if (sourceBytes.Length == 0)
        {
            throw new ArgumentException("Source image is empty.", nameof(sourceBytes));
        }

        var maxEdge = Math.Clamp(maxEdgePx, 64, 1024);
        using var image = Image.Load(sourceBytes);
        if (image.Width > maxEdge || image.Height > maxEdge)
        {
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(maxEdge, maxEdge),
            }));
        }

        await using var output = new MemoryStream();
        await image.SaveAsGifAsync(
                output,
                new GifEncoder
                {
                    ColorTableMode = GifColorTableMode.Global,
                    Quantizer = new WuQuantizer(new QuantizerOptions
                    {
                        MaxColors = 256,
                    }),
                },
                cancellationToken)
            .ConfigureAwait(false);

        var gifBytes = output.ToArray();
        if (!StartsWithGif89a(gifBytes))
        {
            throw new InvalidOperationException("GIF encoder did not produce a GIF89a header.");
        }

        return (gifBytes, image.Width, image.Height);
    }
}
