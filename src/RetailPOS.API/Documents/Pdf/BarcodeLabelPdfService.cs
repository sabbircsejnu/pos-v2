using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RetailPOS.API.DTOs.Barcode;
using ZXing;
using ZXing.Common;

namespace RetailPOS.API.Documents.Pdf;

public interface IBarcodeLabelPdfService
{
    Task<DocumentPdfFileResult> GenerateAsync(RecordBarcodePrintHistoryDto dto, CancellationToken cancellationToken = default);
}

public sealed class BarcodeLabelPdfService : IBarcodeLabelPdfService
{
    public Task<DocumentPdfFileResult> GenerateAsync(RecordBarcodePrintHistoryDto dto, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new InvalidOperationException("At least one barcode item is required to generate PDF.");
        }

        var labelWidthMm = dto.LabelWidthMm > 0 ? (float)dto.LabelWidthMm : 60f;
        var labelHeightMm = dto.LabelHeightMm > 0 ? (float)dto.LabelHeightMm : 40f;
        const float pageContentWidthMm = 194f;
        const float horizontalGapMm = 2f;
        var labelsPerRow = Math.Max(1, (int)Math.Floor((pageContentWidthMm + horizontalGapMm) / (labelWidthMm + horizontalGapMm)));
        var companyName = string.IsNullOrWhiteSpace(dto.CompanyName) ? "Business" : dto.CompanyName.Trim();

        var barcodeCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var flattenedLabels = new List<BarcodeLabelRow>();
        foreach (var item in dto.Items)
        {
            var quantity = Math.Max(1, item.QuantityPrinted);
            var barcodeValue = string.IsNullOrWhiteSpace(item.BarcodeValue) ? item.VariantSku : item.BarcodeValue;
            byte[]? barcodePng = null;
            if (!string.IsNullOrWhiteSpace(barcodeValue))
            {
                barcodePng = GetOrCreateBarcodePng(barcodeCache, barcodeValue);
            }

            for (var i = 0; i < quantity; i++)
            {
                flattenedLabels.Add(new BarcodeLabelRow(
                    item.ProductName,
                    item.VariantSku,
                    barcodeValue,
                    item.SellingPrice,
                    barcodePng));
            }
        }

        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(8, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily("Helvetica").FontSize(8));

                page.Header().PaddingBottom(4).Text("Barcode Labels").FontSize(12).SemiBold();
                page.Content().Element(content => RenderLabelSheet(content, flattenedLabels, labelsPerRow, labelWidthMm, labelHeightMm, horizontalGapMm, companyName));
                page.Footer().AlignRight().Text(text =>
                {
                    text.Span($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

        var fileName = $"barcode-labels-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pdf";
        return Task.FromResult(new DocumentPdfFileResult(bytes, fileName));
    }

    private static void RenderLabelSheet(
        IContainer container,
        IReadOnlyList<BarcodeLabelRow> labels,
        int labelsPerRow,
        float widthMm,
        float heightMm,
        float horizontalGapMm,
        string companyName)
    {
        container.Column(column =>
        {
            column.Spacing(1.6f, Unit.Millimetre);

            foreach (var rowLabels in labels.Chunk(labelsPerRow))
            {
                column.Item().Row(row =>
                {
                    row.Spacing(horizontalGapMm, Unit.Millimetre);

                    foreach (var label in rowLabels)
                    {
                        row.ConstantItem(widthMm, Unit.Millimetre)
                            .Element(cell => RenderLabel(cell, label, widthMm, heightMm, companyName));
                    }
                });
            }
        });
    }

    private static void RenderLabel(IContainer container, BarcodeLabelRow label, float widthMm, float heightMm, string companyName)
    {
        container
            .Width(widthMm, Unit.Millimetre)
            .Height(heightMm, Unit.Millimetre)
            .Border(0.35f)
            .BorderColor(Colors.Grey.Medium)
            .Padding(1.4f, Unit.Millimetre)
            .Column(column =>
            {
                column.Spacing(0.8f, Unit.Millimetre);

                column.Item().AlignCenter().Text(companyName).FontSize(8).SemiBold();

                var title = BuildRetailTitle(label);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    column.Item().AlignCenter().Text(title).FontSize(8).SemiBold().LineHeight(1.12f);
                }

                var barcodeText = label.BarcodeValue;
                if (!string.IsNullOrWhiteSpace(barcodeText))
                {
                    if (label.BarcodePng != null)
                    {
                        column.Item()
                            .PaddingTop(0.4f, Unit.Millimetre)
                            .AlignCenter().Height(10.5f, Unit.Millimetre)
                            .Image(label.BarcodePng).FitArea();
                    }
                    else
                    {
                        column.Item().AlignCenter().Text(barcodeText).FontFamily("Courier New").FontSize(7).SemiBold();
                    }
                }

                if (label.SellingPrice.HasValue)
                {
                    column.Item().AlignCenter().Text($"MRP: {label.SellingPrice.Value:0.##}").FontSize(8).SemiBold();
                }
            });
    }

    private static string BuildRetailTitle(BarcodeLabelRow label)
    {
        var mainProductCode = ExtractMainProductCode(label.Sku);
        var productName = label.ProductName?.Trim();

        if (!string.IsNullOrWhiteSpace(mainProductCode) && !string.IsNullOrWhiteSpace(productName))
        {
            return $"{mainProductCode} - {productName}";
        }

        if (!string.IsNullOrWhiteSpace(mainProductCode))
        {
            return mainProductCode;
        }

        return productName ?? string.Empty;
    }

    private static string ExtractMainProductCode(string? variantSku)
    {
        var sku = variantSku?.Trim();
        if (string.IsNullOrWhiteSpace(sku))
        {
            return string.Empty;
        }

        var dashIndex = sku.IndexOf('-');
        return dashIndex > 0 ? sku[..dashIndex] : sku;
    }

    private sealed record BarcodeLabelRow(
        string ProductName,
        string Sku,
        string? BarcodeValue,
        decimal? SellingPrice,
        byte[]? BarcodePng);

    private static byte[] GetOrCreateBarcodePng(IDictionary<string, byte[]> cache, string barcodeValue)
    {
        if (cache.TryGetValue(barcodeValue, out var existing))
        {
            return existing;
        }

        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Height = 80,
                Width = Math.Max(200, barcodeValue.Length * 18),
                Margin = 0,
                PureBarcode = true
            }
        };

        var pixelData = writer.Write(barcodeValue);
        using var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(pixelData.Pixels, pixelData.Width, pixelData.Height);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        var png = stream.ToArray();
        cache[barcodeValue] = png;
        return png;
    }
}
