using System.Text.RegularExpressions;
using Almutamakkin.BarcodeAgent.Configuration;
using Microsoft.Extensions.Options;

namespace Almutamakkin.BarcodeAgent.Printing;

public sealed record BarcodePrintabilityResult(
    bool Printable,
    string? Reason,
    string BarcodeType,
    int EstimatedWidthDots);

public interface IBarcodePrintability
{
    BarcodePrintabilityResult Analyze(string? barcode);
}

public sealed partial class BarcodePrintability(IOptions<PrinterOptions> options) : IBarcodePrintability
{
    private readonly int _labelWidthDots =
        (int)Math.Round(options.Value.LabelWidthMm * options.Value.Dpi / 25.4, MidpointRounding.AwayFromZero);
    private const int BarcodeX = 52;
    private const int NarrowModuleDots = 2;
    private const int RightQuietZoneDots = 20;

    public BarcodePrintabilityResult Analyze(string? barcode)
    {
        var value = barcode?.Trim() ?? string.Empty;
        if (value.Length == 0) return Rejected("Barcode is empty.");
        if (!SafeCharacters().IsMatch(value))
            return Rejected("Barcode contains characters unsupported by the printer.");

        if (value.Length == 13 && value.All(char.IsDigit) && HasValidGtinCheckDigit(value))
            return Fit("EAN13", 95 * NarrowModuleDots);
        if (value.Length == 8 && value.All(char.IsDigit) && HasValidGtinCheckDigit(value))
            return Fit("EAN8", 67 * NarrowModuleDots);

        int dataCodewords;
        if (value.All(char.IsDigit) && value.Length % 2 == 0)
            dataCodewords = value.Length / 2;
        else if (value.Length <= 7)
            dataCodewords = value.Length;
        else
            return Rejected("Barcode is too long for a readable 38 mm Code 128 label.");

        var width = NarrowModuleDots * ((11 * dataCodewords) + 35);
        return Fit("128", width);
    }

    private BarcodePrintabilityResult Fit(string type, int width)
    {
        var fits = BarcodeX + width + RightQuietZoneDots <= _labelWidthDots;
        return fits
            ? new BarcodePrintabilityResult(true, null, type, width)
            : new BarcodePrintabilityResult(false, "Barcode is too wide for a readable 38 mm label.", type, width);
    }

    private static BarcodePrintabilityResult Rejected(string reason) => new(false, reason, "", 0);

    private static bool HasValidGtinCheckDigit(string value)
    {
        var sum = 0;
        for (var i = value.Length - 2; i >= 0; i--)
        {
            var digit = value[i] - '0';
            sum += digit * (((value.Length - 1 - i) % 2 == 1) ? 3 : 1);
        }
        return (10 - (sum % 10)) % 10 == value[^1] - '0';
    }

    [GeneratedRegex("^[0-9A-Za-z._/+\\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeCharacters();
}
