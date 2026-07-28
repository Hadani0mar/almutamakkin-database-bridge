using Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class ProductPhotoGifEncoderTests
{
    [Fact]
    public async Task EncodeAsync_Produces_Gif89a_Header()
    {
        await using var png = new MemoryStream();
        using (var image = new Image<Rgba32>(32, 24))
        {
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    image[x, y] = new Rgba32(40, 120, 200, 255);
                }
            }

            await image.SaveAsPngAsync(png);
        }

        var (gifBytes, width, height) = await ProductPhotoGifEncoder.EncodeAsync(
            png.ToArray(),
            maxEdgePx: 64,
            CancellationToken.None);

        Assert.True(ProductPhotoGifEncoder.StartsWithGif89a(gifBytes));
        Assert.Equal(0x47, gifBytes[0]);
        Assert.Equal(0x49, gifBytes[1]);
        Assert.Equal(0x46, gifBytes[2]);
        Assert.Equal(0x38, gifBytes[3]);
        Assert.Equal(0x39, gifBytes[4]);
        Assert.Equal(0x61, gifBytes[5]);
        Assert.True(width <= 64);
        Assert.True(height <= 64);
        Assert.True(gifBytes.Length > 6);
    }
}
