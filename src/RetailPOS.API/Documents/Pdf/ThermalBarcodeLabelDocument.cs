using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ZXing;
using ZXing.OneD;

namespace RetailPOS.API.Documents.Pdf;

public sealed record ThermalBarcodeLabel(
    string Company,
    string Title,
    string VariantLine,
    string BarcodeValue,
    string PriceText);

/// <summary>One PDF page per label, sized to the label stock, with no header or footer.</summary>
public static class ThermalBarcodeLabelDocument
{
    private const int QuietZoneModules = 10;

    public static IDocument Create(IReadOnlyList<ThermalBarcodeLabel> labels, ThermalLabelSettings settings) =>
        Document.Create(container =>
        {
            foreach (var label in labels)
            {
                container.Page(page =>
                {
                    page.Size((float)settings.WidthMm, (float)settings.HeightMm, Unit.Millimetre);
                    page.MarginTop((float)settings.MarginTopMm, Unit.Millimetre);
                    page.MarginRight((float)settings.MarginRightMm, Unit.Millimetre);
                    page.MarginBottom((float)settings.MarginBottomMm, Unit.Millimetre);
                    page.MarginLeft((float)settings.MarginLeftMm, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(style => style.FontFamily("Arial", "Helvetica").Bold().FontColor(Colors.Black));
                    page.Content().AlignMiddle().Element(content => RenderLabel(content, label, settings));
                });
            }
        });

    private static void RenderLabel(IContainer container, ThermalBarcodeLabel label, ThermalLabelSettings settings)
    {
        var layout = ThermalLabelLayout.Compute(settings, label.Company, label.Title, label.VariantLine);

        container.Column(column =>
        {
            column.Spacing((float)ThermalLabelLayout.RowGapMm, Unit.Millimetre);
            RenderRow(column, label.Company, layout.Company);
            RenderRow(column, label.Title, layout.Title);
            RenderRow(column, label.VariantLine, layout.Variant);
            column.Item()
                .Height((float)layout.BarcodeHeightMm, Unit.Millimetre)
                .Svg(BuildBarcodeSvg(label.BarcodeValue, layout.ContentWidthMm, layout.BarcodeHeightMm))
                .FitArea();
            RenderRow(column, label.PriceText, layout.Price);
        });
    }

    private static void RenderRow(ColumnDescriptor column, string text, FittedLabelText fitted)
    {
        if (fitted.Lines == 0)
            return;

        // ScaleToFit guards against a font's metrics exceeding the reserved box, which would otherwise abort the document.
        column.Item()
            .Height((float)fitted.HeightMm, Unit.Millimetre)
            .ScaleToFit()
            .Text(text.Trim())
            .AlignCenter()
            .FontSize(MmToPoints(fitted.FontMm))
            .LineHeight((float)ThermalLabelLayout.LineHeight)
            .ClampLines(fitted.Lines, "…");
    }

    /// <summary>
    /// CODE128, as elsewhere in the module, drawn as vector bars at the box's exact aspect ratio with the quiet
    /// zone inside the image, so nothing is resampled and a scanner can find the ends of the code.
    /// </summary>
    public static string BuildBarcodeSvg(string value, double widthMm, double heightMm)
    {
        var bars = new Code128Writer().encode(
            value,
            BarcodeFormat.CODE_128,
            0,
            1,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 });

        var viewWidth = bars.Width + QuietZoneModules * 2;
        var viewHeight = (viewWidth * heightMm / widthMm).ToString("0.###", CultureInfo.InvariantCulture);
        var svg = new StringBuilder()
            .Append($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {viewWidth} {viewHeight}" shape-rendering="crispEdges">""")
            .Append($"""<rect width="{viewWidth}" height="{viewHeight}" fill="#ffffff"/>""");

        for (var x = 0; x < bars.Width; x++)
        {
            if (!bars[x, 0])
                continue;

            var start = x;
            while (x + 1 < bars.Width && bars[x + 1, 0])
                x++;

            svg.Append($"""<rect x="{start + QuietZoneModules}" width="{x - start + 1}" height="{viewHeight}" fill="#000000"/>""");
        }

        return svg.Append("</svg>").ToString();
    }

    private static float MmToPoints(double mm) => (float)(mm * 72 / 25.4);
}
