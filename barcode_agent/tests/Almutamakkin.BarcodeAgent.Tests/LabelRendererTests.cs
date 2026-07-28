using System.Text;
using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Models;
using Almutamakkin.BarcodeAgent.Printing;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Tests;

public sealed class LabelRendererTests
{
    private readonly BarcodePrintability _printability = new(Options.Create(new PrinterOptions()));
    private readonly LabelRenderer _renderer;

    public LabelRendererTests()
    {
        _renderer = new LabelRenderer(Options.Create(new PrinterOptions()), _printability);
    }

    [Fact]
    public void Render_MatchesVerifiedXp365bLayout()
    {
        var product = Product("Empcoza Trio XR 10/5/1000MG", "6224002652119");
        var payload = _renderer.Render("صيدلية الطبي", product, 3);
        var printable = Encoding.ASCII.GetString(payload);

        Assert.Contains("SIZE 38 mm,25 mm\r\n", printable);
        Assert.Contains("BITMAP 0,0,38,38,0,", printable);
        Assert.Contains("TEXT 20,42,\"1\",0,1,1,\"Empcoza Trio XR 10/5/1000MG\"", printable);
        Assert.Contains("BARCODE 52,65,\"EAN13\",100,1,0,2,4,\"6224002652119\"", printable);
        Assert.EndsWith("PRINT 1,3\r\n", printable);
    }

    [Fact]
    public void Render_RasterizesNonAsciiProductName()
    {
        var payload = _renderer.Render("صيدلية الطبي", Product("دواء تجريبي", "ABC-123"), 1);
        var printable = Encoding.ASCII.GetString(payload);

        Assert.Contains("BITMAP 0,42,38,20,0,", printable);
        Assert.DoesNotContain("TEXT 20,42", printable);
        Assert.Contains("BARCODE 52,65,\"128\"", printable);
    }

    [Theory]
    [InlineData("6224002652119", "EAN13")]
    [InlineData("ABC-123", "128")]
    public void ResolveBarcodeType_SelectsSupportedNativeCommand(string barcode, string expected) =>
        Assert.Equal(expected, _printability.Analyze(barcode).BarcodeType);

    private static ProductDto Product(string name, string barcode) =>
        new(1, 2, name, barcode, 4, 33, 7, "علبة", 1, 20, "Supplier", true, null);
}
