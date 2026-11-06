namespace RetailPOS.API.Documents.Pdf;

public sealed record ThermalLabelSettings(
    double WidthMm,
    double HeightMm,
    double MarginTopMm,
    double MarginRightMm,
    double MarginBottomMm,
    double MarginLeftMm);

public sealed record FittedLabelText(double FontMm, int Lines, double HeightMm);

public sealed record ThermalLabelLayoutResult(
    double ContentWidthMm,
    double ContentHeightMm,
    FittedLabelText Company,
    FittedLabelText Title,
    FittedLabelText Variant,
    FittedLabelText Price,
    double BarcodeHeightMm);

/// <summary>
/// Sizes every row of a thermal label. This is a copy of computeThermalLabelLayout in the frontend's
/// barcode-label-print.ts; both are pinned to the same expected values so browser print and PDF produce
/// identical boxes. Change them together.
/// </summary>
public static class ThermalLabelLayout
{
    public const double RowGapMm = 0.4;
    public const double LineHeight = 1.15;

    private const double MaxLabelMm = 300;
    private const double MaxMarginMm = 20;
    private const double MinContentWidthMm = 24;
    private const double MinContentHeightMm = 16;
    private const double MinBarcodeHeightMm = 6;
    private const double MaxBarcodeHeightToWidth = 0.6;
    private const double WrapSlack = 1.08;

    private static readonly FittedLabelText Hidden = new(0, 0, 0);

    public static string? Validate(ThermalLabelSettings settings)
    {
        if (settings.WidthMm is < 1 or > MaxLabelMm || settings.HeightMm is < 1 or > MaxLabelMm)
            return $"Label width and height must be between 1 and {MaxLabelMm} mm.";

        if (IsInvalidMargin(settings.MarginTopMm) || IsInvalidMargin(settings.MarginRightMm)
            || IsInvalidMargin(settings.MarginBottomMm) || IsInvalidMargin(settings.MarginLeftMm))
            return $"Printer margins must be between 0 and {MaxMarginMm} mm.";

        if (settings.WidthMm - settings.MarginLeftMm - settings.MarginRightMm < MinContentWidthMm)
            return $"Label width minus left and right margins must be at least {MinContentWidthMm} mm.";

        if (settings.HeightMm - settings.MarginTopMm - settings.MarginBottomMm < MinContentHeightMm)
            return $"Label height minus top and bottom margins must be at least {MinContentHeightMm} mm.";

        return null;
    }

    public static ThermalLabelLayoutResult Compute(
        ThermalLabelSettings settings,
        string company,
        string title,
        string variantLine)
    {
        var contentWidthMm = settings.WidthMm - settings.MarginLeftMm - settings.MarginRightMm;
        var contentHeightMm = settings.HeightMm - settings.MarginTopMm - settings.MarginBottomMm;
        double Scaled(double factor, double min, double max) => Math.Min(max, Math.Max(min, contentHeightMm * factor));
        var titleLines = contentHeightMm >= 30 ? 3 : contentHeightMm >= 20 ? 2 : 1;

        var fittedCompany = FitText(company, contentWidthMm, Scaled(0.11, 2.2, 3.4), 1.9, 1);
        var fittedTitle = FitText(title, contentWidthMm, Scaled(0.115, 2.2, 3.4), 1.9, titleLines);
        var fittedVariant = FitText(variantLine, contentWidthMm, Scaled(0.095, 2, 3), 1.7, 1);
        var priceFont = Math.Floor(Scaled(0.125, 2.4, 3.8) * 10) / 10;
        var fittedPrice = new FittedLabelText(priceFont, 1, RowHeight(priceFont, 1));

        double Available(params FittedLabelText[] rows)
        {
            var visible = rows.Where(row => row.Lines > 0).ToArray();
            return contentHeightMm - visible.Sum(row => row.HeightMm) - visible.Length * RowGapMm;
        }

        // On very short labels the barcode matters more than the variant line, which is the first row to go.
        if (fittedVariant.Lines > 0
            && Available(fittedCompany, fittedTitle, fittedVariant, fittedPrice) < MinBarcodeHeightMm)
        {
            fittedVariant = Hidden;
        }

        var barcodeHeightMm = Math.Min(
            Available(fittedCompany, fittedTitle, fittedVariant, fittedPrice),
            contentWidthMm * MaxBarcodeHeightToWidth);

        return new ThermalLabelLayoutResult(
            contentWidthMm,
            contentHeightMm,
            fittedCompany,
            fittedTitle,
            fittedVariant,
            fittedPrice,
            RoundMm(barcodeHeightMm));
    }

    /// <summary>Variant name and SKU for the label, leaving out anything that would only repeat other text.</summary>
    public static string BuildVariantLine(string? variantName, string? sku, string? productName)
    {
        var trimmedSku = sku?.Trim() ?? string.Empty;
        var name = variantName?.Trim() ?? string.Empty;
        var nameApplies = name.Length > 0
            && !name.Equals("default", StringComparison.OrdinalIgnoreCase)
            && !name.Equals(productName?.Trim(), StringComparison.OrdinalIgnoreCase)
            && !name.Equals(trimmedSku, StringComparison.OrdinalIgnoreCase);

        return string.Join(" | ", new[] { nameApplies ? name : string.Empty, trimmedSku }.Where(part => part.Length > 0));
    }

    private static FittedLabelText FitText(string? text, double widthMm, double maxFontMm, double minFontMm, int maxLines)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
            return Hidden;

        var widthEm = EstimateWidthEm(value);
        var maxTenths = (int)Math.Floor(maxFontMm * 10);
        var minTenths = (int)Math.Round(minFontMm * 10, MidpointRounding.AwayFromZero);
        for (var tenths = maxTenths; tenths >= minTenths; tenths--)
        {
            var fontMm = tenths / 10d;
            var lineWidthMm = widthEm * fontMm;
            var lines = lineWidthMm <= widthMm ? 1 : (int)Math.Ceiling(lineWidthMm * WrapSlack / widthMm);
            if (lines <= maxLines)
                return new FittedLabelText(fontMm, lines, RowHeight(fontMm, lines));
        }

        // Still too long at the smallest legible size: the text is clamped to the box with an ellipsis.
        var smallest = minTenths / 10d;
        return new FittedLabelText(smallest, maxLines, RowHeight(smallest, maxLines));
    }

    /// <summary>Deliberately generous per-character widths for bold sans-serif text, so an estimate rarely undershoots.</summary>
    private static double EstimateWidthEm(string text)
    {
        var em = 0d;
        foreach (var rune in text.EnumerateRunes())
        {
            em += rune.Value switch
            {
                ' ' => 0.28,
                >= 'A' and <= 'Z' => 0.72,
                _ => 0.56
            };
        }

        return em;
    }

    private static double RowHeight(double fontMm, int lines) => RoundMm(fontMm * LineHeight * lines);

    private static double RoundMm(double value) => Math.Round(value * 100, MidpointRounding.AwayFromZero) / 100;

    private static bool IsInvalidMargin(double value) => value is < 0 or > MaxMarginMm or double.NaN;
}
