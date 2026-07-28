using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text;
using Almutamakkin.BarcodeAgent.Configuration;
using Almutamakkin.BarcodeAgent.Models;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Printing;

public interface ILabelRenderer
{
    byte[] Render(string businessName, ProductDto product, int copies);
}

public sealed class LabelRenderer(
    IOptions<PrinterOptions> options,
    IBarcodePrintability printability) : ILabelRenderer
{
    private readonly PrinterOptions _options = options.Value;

    public byte[] Render(string businessName, ProductDto product, int copies)
    {
        if (string.IsNullOrWhiteSpace(businessName)) throw new InvalidOperationException("Business name is unavailable.");
        if (string.IsNullOrWhiteSpace(product.Barcode)) throw new InvalidOperationException("Product barcode is empty.");
        if (copies < 1 || copies > _options.MaximumCopies) throw new ArgumentOutOfRangeException(nameof(copies));
        var barcodeValidation = printability.Analyze(product.Barcode);
        if (!barcodeValidation.Printable) throw new InvalidOperationException(barcodeValidation.Reason);

        var labelWidthDots = MmToDots(_options.LabelWidthMm);
        var header = RenderInverseMonochromeText(businessName.Trim(), labelWidthDots, 38, 21, rightToLeft: true);
        var barcodeType = barcodeValidation.BarcodeType;
        var safeName = SanitizePrinterText(product.Name, 33);

        using var stream = new MemoryStream();
        WriteAscii(stream, $"SIZE {_options.LabelWidthMm} mm,{_options.LabelHeightMm} mm\r\n");
        WriteAscii(stream, $"GAP {_options.GapMm} mm,0 mm\r\nSPEED {_options.Speed}\r\nDENSITY {_options.Density}\r\nDIRECTION 1\r\nCLS\r\n");
        WriteAscii(stream, $"BITMAP 0,0,{header.WidthBytes},{header.Height},0,");
        stream.Write(header.Data);
        WriteAscii(stream, "\r\n");
        if (safeName.Any(character => character > 127))
        {
            var productName = RenderInverseMonochromeText(safeName, labelWidthDots, 20, 13, rightToLeft: true);
            WriteAscii(stream, $"BITMAP 0,42,{productName.WidthBytes},{productName.Height},0,");
            stream.Write(productName.Data);
            WriteAscii(stream, "\r\n");
        }
        else
        {
            WriteAscii(stream, $"TEXT 20,42,\"1\",0,1,1,\"{safeName}\"\r\n");
        }
        WriteAscii(stream, $"BARCODE 52,65,\"{barcodeType}\",100,1,0,2,4,\"{product.Barcode}\"\r\n");
        WriteAscii(stream, $"PRINT 1,{copies}\r\n");
        return stream.ToArray();
    }

    private HeaderBitmap RenderInverseMonochromeText(
        string text,
        int width,
        int height,
        int fontSize,
        bool rightToLeft)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            using var font = new Font(_options.BusinessNameFont, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = rightToLeft ? StringFormatFlags.DirectionRightToLeft : 0
            };
            graphics.DrawString(text, font, Brushes.Black, new RectangleF(0, 0, width, height), format);
        }

        var widthBytes = (width + 7) / 8;
        var data = new byte[widthBytes * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            // Verified XP-365B queue requires inverse polarity: light pixels are set.
            if (bitmap.GetPixel(x, y).GetBrightness() >= 0.5f)
                data[(y * widthBytes) + (x / 8)] |= (byte)(0x80 >> (x % 8));
        }
        return new HeaderBitmap(widthBytes, height, data);
    }

    private int MmToDots(int mm) => (int)Math.Round(mm * _options.Dpi / 25.4, MidpointRounding.AwayFromZero);

    private static string SanitizePrinterText(string value, int maxLength) =>
        value.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ").Trim() is var cleaned
            ? cleaned[..Math.Min(cleaned.Length, maxLength)]
            : string.Empty;

    private static void WriteAscii(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));

    private sealed record HeaderBitmap(int WidthBytes, int Height, byte[] Data);

}
