using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using RetailPOS.API.Documents.Pdf;
using RetailPOS.API.DTOs.Barcode;
using RetailPOS.Tests.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;

namespace RetailPOS.Tests.Barcodes;

public class ThermalBarcodeLabelTests : IClassFixture<RetailPosApiFactory>
{
    private const string LongTitle = "ABA424P - Borka Embro Gher Panel Black With Extra Long Designer Stone Work Collection Edition 2026";

    private readonly RetailPosApiFactory _factory;

    public ThermalBarcodeLabelTests(RetailPosApiFactory factory)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _factory = factory;
    }

    private static ThermalLabelSettings Settings(double width, double height, double margin = 1) =>
        new(width, height, margin, margin, margin, margin);

    // The same cases and values are pinned in barcode-label-print.spec.ts, so both renderers lay labels out identically.
    public static TheoryData<string, double, double, double, string, string, string, double[]> SharedLayoutCases => new()
    {
        { "short 40x25", 40, 25, 1, "AHIR BORKA BAZAR", "ABA917P - Australian Orenge", "Per ML | ABA917P", [2.5, 2.88, 2.6, 2, 5.98, 2.1, 1, 2.42, 2.8, 3.22, 6.9] },
        { "long 50x25", 50, 25, 1, "AHIR BORKA BAZAR", LongTitle, "Black / Size 56 | ABA424P-BLK-56", [2.5, 2.88, 1.9, 2, 4.37, 2.1, 1, 2.42, 2.8, 3.22, 8.51] },
        { "long 60x40", 60, 40, 1.5, "IraniMart", LongTitle, "Black / Size 56 | ABA424P-BLK-56", [3.4, 3.91, 2.9, 3, 10, 3, 1, 3.45, 3.8, 4.37, 13.67] },
        { "shelf 80x50", 80, 50, 1, "IraniMart", "Short", "", [3.4, 3.91, 3.4, 1, 3.91, 0, 0, 0, 3.8, 4.37, 34.61] },
        { "tiny 26x18", 26, 18, 0, "IraniMart", LongTitle, "Black / Size 56 | ABA424P-BLK-56", [2.2, 2.53, 1.9, 1, 2.18, 1.7, 1, 1.95, 2.4, 2.76, 6.98] },
    };

    [Theory]
    [MemberData(nameof(SharedLayoutCases))]
    public void Layout_MatchesTheValuesPinnedInTheFrontend(
        string name, double width, double height, double margin, string company, string title, string variant, double[] expected)
    {
        var layout = ThermalLabelLayout.Compute(Settings(width, height, margin), company, title, variant);

        double[] actual =
        [
            layout.Company.FontMm, layout.Company.HeightMm,
            layout.Title.FontMm, layout.Title.Lines, layout.Title.HeightMm,
            layout.Variant.FontMm, layout.Variant.Lines, layout.Variant.HeightMm,
            layout.Price.FontMm, layout.Price.HeightMm,
            layout.BarcodeHeightMm
        ];
        Assert.True(expected.SequenceEqual(actual), $"{name}: [{string.Join(", ", actual)}]");
    }

    [Theory]
    [InlineData(40, 25)]
    [InlineData(50, 25)]
    [InlineData(60, 40)]
    [InlineData(80, 50)]
    public void Layout_LongTextNeverOverflowsTheLabelOrSqueezesTheBarcode(double width, double height)
    {
        var layout = ThermalLabelLayout.Compute(Settings(width, height), LongTitle, LongTitle + LongTitle, LongTitle);

        var rows = new[] { layout.Company, layout.Title, layout.Variant, layout.Price }.Where(row => row.Lines > 0).ToArray();
        var used = rows.Sum(row => row.HeightMm) + rows.Length * ThermalLabelLayout.RowGapMm + layout.BarcodeHeightMm;
        Assert.InRange(used, 0, layout.ContentHeightMm + 0.01);
        Assert.True(layout.BarcodeHeightMm >= 6, $"barcode height {layout.BarcodeHeightMm}");
    }

    [Fact]
    public async Task Pdf_PrintsOnePagePerLabel_AtTheExactLabelSize_InQueueOrder()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/barcodes/pdf", ThermalPayload(50, 25, ("CM00181", 3), ("CM00917", 2)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var boxes = ReadMediaBoxes(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(5, boxes.Count);
        Assert.All(boxes, box =>
        {
            Assert.Equal(50, PointsToMm(box.Width), 1);
            Assert.Equal(25, PointsToMm(box.Height), 1);
        });
    }

    [Fact]
    public void RenderedLabels_ScanBackToTheirOwnValues_InSequence_EvenWithLongNames()
    {
        var labels = new[] { "CM00181", "CM00181", "ABA917P-100ML", "CM00181" }
            .Select(value => new ThermalBarcodeLabel("AHIR BORKA BAZAR", LongTitle, "Black / Size 56 | ABA424P-BLK-56", value, "MRP: 7,490.00"))
            .ToList();

        var pages = ThermalBarcodeLabelDocument.Create(labels, Settings(50, 25))
            .GenerateImages(new ImageGenerationSettings { RasterDpi = 300 })
            .ToList();

        Assert.Equal(labels.Select(label => label.BarcodeValue), pages.Select(Decode));
    }

    [Fact]
    public async Task Pdf_NamesNoLayout_KeepsTheExistingA4Sheet()
    {
        using var client = CreateClient();
        var payload = ThermalPayload(60, 40, ("CM00181", 2));
        payload.PrintLayout = null;

        var response = await client.PostAsJsonAsync("/api/barcodes/pdf", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var box = Assert.Single(ReadMediaBoxes(await response.Content.ReadAsByteArrayAsync()));
        Assert.Equal(210, PointsToMm(box.Width), 0);
        Assert.Equal(297, PointsToMm(box.Height), 0);
    }

    [Theory]
    [InlineData(20, 25, 1, "width")]
    [InlineData(50, 15, 1, "height")]
    [InlineData(50, 25, 25, "margins")]
    public async Task Pdf_RejectsSizesTheLabelCannotHold(decimal width, decimal height, decimal margin, string expectedWord)
    {
        using var client = CreateClient();
        var payload = ThermalPayload(width, height, ("CM00181", 1));
        payload.MarginLeftMm = payload.MarginTopMm = margin;

        var response = await client.PostAsJsonAsync("/api/barcodes/pdf", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedWord, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pdf_RefusesAnItemWithNothingToEncode_InsteadOfInventingABarcode()
    {
        using var client = CreateClient();
        var payload = ThermalPayload(50, 25, ("CM00181", 1));
        payload.Items[0].BarcodeValue = " ";
        payload.Items[0].VariantSku = " ";

        var response = await client.PostAsJsonAsync("/api/barcodes/pdf", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no barcode or SKU", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Black / Size 56", "ABA424P-BLK-56", "Borka", "Black / Size 56 | ABA424P-BLK-56")]
    [InlineData("Default", "ABA424P", "Borka", "ABA424P")]
    [InlineData("Borka", "ABA424P", "Borka", "ABA424P")]
    [InlineData("ABA424P", "ABA424P", "Borka", "ABA424P")]
    [InlineData("Per ML", "", "Orange", "Per ML")]
    public void VariantLine_LeavesOutOnlyRepeatedText(string name, string sku, string product, string expected)
    {
        Assert.Equal(expected, ThermalLabelLayout.BuildVariantLine(name, sku, product));
    }

    private static string? Decode(byte[] png)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(png);
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.CODE_128],
                TryHarder = true
            }
        };
        return reader.Decode(pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.RGBA32)?.Text;
    }

    private static List<(double Width, double Height)> ReadMediaBoxes(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        return Regex.Matches(text, @"/MediaBox\s*\[\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\]")
            .Select(match => (
                double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static double PointsToMm(double points) => points * 25.4 / 72;

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7001");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "barcode.view,barcode.print,barcode.bulk_print");
        return client;
    }

    private static RecordBarcodePrintHistoryDto ThermalPayload(decimal width, decimal height, params (string Barcode, int Quantity)[] items) => new()
    {
        CompanyName = "AHIR BORKA BAZAR",
        PrintLayout = "thermal",
        PrintMode = "pdf",
        LabelWidthMm = width,
        LabelHeightMm = height,
        Items = items.Select((item, index) => new CreateBarcodePrintHistoryItemDto
        {
            VariantId = 1000 + index,
            ProductName = "Borka Embro Gher Panel Black",
            VariantName = "Black / Size 56",
            VariantSku = "ABA424P-BLK-56",
            MainProductCode = "ABA424P",
            BarcodeValue = item.Barcode,
            SellingPrice = 7490,
            PriceText = "MRP: 7,490.00",
            QuantityPrinted = item.Quantity
        }).ToList()
    };
}
