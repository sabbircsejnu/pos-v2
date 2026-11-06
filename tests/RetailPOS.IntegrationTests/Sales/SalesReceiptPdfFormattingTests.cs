using Microsoft.Extensions.Configuration;
using QuestPDF.Infrastructure;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Infrastructure.Sales;
using UglyToad.PdfPig;

namespace RetailPOS.IntegrationTests.Sales;

public sealed class SalesReceiptPdfFormattingTests
{
    [Fact]
    public async Task ReceiptSettingsAndPdfUseConfiguredCompanyDateFormat()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"company":{"dateFormat":"DD/MM/YYYY"}}""");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["SystemSettings:FilePath"] = path }).Build();
            var settings = await new SaleReceiptSettingsProvider(configuration).GetAsync(CancellationToken.None);
            Assert.Equal("DD/MM/YYYY", settings.DateFormat);
            var timestamp = new DateTime(2026, 10, 22, 9, 30, 0, DateTimeKind.Utc);
            var receipt = new SaleReceiptDocumentReadModel(
                "A4",
                new(false, null, "IraniMart", "Outlet", null, null, null, null),
                new("SALE-1", timestamp, "Cashier", "Terminal", "Customer"),
                [],
                new(0, 0, 0, 0, 0, 0, 0, "Cash", 0, 0),
                new(null, null, null, false, false, null, timestamp))
                { DateFormat = settings.DateFormat };

            using var pdf = PdfDocument.Open(new SaleReceiptPdfRenderer().Render(receipt));
            var text = string.Join('\n', pdf.GetPages().Select(page => page.Text));
            Assert.Contains("Date: 22/10/2026 09:30:00", text);
            Assert.Contains("Generated: 22/10/2026 09:30:00", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SaleReceiptDocumentReadModel ThermalReceipt(int itemCount = 1) => new(
        "80mm",
        new(false, null, "AHIR BORKA BAZAR", "Outlet", "Bashundhara City", null, null, null)
        {
            ConcernOf = "PARENT GROUP",
            BranchName = "IRANI MART-1",
            ShowroomHotline = "+8801898806901",
            Website = "www.example.test",
            TaxIdentityLine = "BIN: 0029807530203, Mushak-6.3"
        },
        new("SLBL-1-C-10511266", new DateTime(2026, 11, 5, 10, 20, 0, DateTimeKind.Utc), "Cashier", "Terminal", "MD ROMIJ ALOM")
        {
            CustomerMobile = "01711039823",
            InvoiceDateText = "05-Nov-2026 04:20 PM"
        },
        Enumerable.Range(0, itemCount)
            .Select(_ => new SaleReceiptLineItemReadModel("Orna Hijab", "ABA1010P", 1, 1290m, 0m, 1290m) { PrintName = "Orna Hijab (ABA1010P)" })
            .ToArray(),
        new(1, 1, 1290m, 0, 0, 0, 1290m, "cash", 1290m, 0),
        new("Thank You", "No refunds.\n<script>alert(1)</script>", "Powered by: Test", false, false, null, DateTime.UtcNow)
        {
            VatLawNotification = "S.R.O. No. 19"
        })
    {
        TaxInclusive = true,
        AmountInWords = "One Thousand Two Hundred Ninety Taka Only",
        PaymentLines = [new("Cash Amount", 1290m)]
    };

    [Fact]
    public void ThermalPdf_PrintsTheLegacyInvoiceLayoutOnAnEightyMillimetrePage()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        using var pdf = PdfDocument.Open(new SaleReceiptPdfRenderer().Render(ThermalReceipt()));
        var page = Assert.Single(pdf.GetPages());
        var text = page.Text;

        Assert.Equal(80, page.Width * 25.4 / 72, 0);
        foreach (var expected in new[]
                 {
                     "AHIR BORKA BAZAR", "A concern of PARENT GROUP", "Branch: IRANI MART-1", "Address: Bashundhara City",
                     "Showroom Hotline: +8801898806901", "Web: www.example.test", "BIN: 0029807530203, Mushak-6.3", "Invoice",
                     "05-Nov-2026 04:20 PM", "SLBL-1-C-10511266", "MD ROMIJ ALOM", "01711039823", "U.Price", "Dis%",
                     "Orna Hijab", "(ABA1010P)", "All prices are inclusive of VAT.", "Sub Total", "(+/-) Rounding", "Amount Due",
                     "Paid", "Cash Amount", "Change", "One Thousand Two Hundred Ninety Taka Only", "Return / Exchange Policy",
                     "S.R.O. No. 19", "Thank You", "Powered by: Test"
                 })
        {
            Assert.Contains(expected, text);
        }

        Assert.Contains("<script>alert(1)</script>", text);
        Assert.DoesNotContain("Cashier", text);
        Assert.DoesNotContain("Generated", text);
    }

    [Fact]
    public void ThermalPdf_ShowsTheDiscountAndPaymentRowsOnlyWhenTheyAreAboveZero()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var withoutDiscount = ThermalReceipt();

        using var plain = PdfDocument.Open(new SaleReceiptPdfRenderer().Render(withoutDiscount));
        var plainText = Assert.Single(plain.GetPages()).Text;
        Assert.DoesNotContain("Discount", plainText);
        Assert.DoesNotContain("Card Amount", plainText);
        Assert.DoesNotContain("Mobile Banking Amount", plainText);
        Assert.Contains("Change", plainText);

        var discounted = withoutDiscount with
        {
            Summary = withoutDiscount.Summary with { Discount = 50m },
            PaymentLines = [new("Mobile Banking Amount", 1290m)]
        };
        using var withDiscount = PdfDocument.Open(new SaleReceiptPdfRenderer().Render(discounted));
        var discountedText = Assert.Single(withDiscount.GetPages()).Text;
        Assert.Contains("Discount", discountedText);
        Assert.Contains("50.00", discountedText);
        Assert.Contains("Mobile Banking Amount", discountedText);
        Assert.DoesNotContain("Cash Amount", discountedText);
    }

    [Theory]
    [InlineData("80mm")]
    [InlineData("A4")]
    public void Pdf_PrintsThePlatformFooterOnlyWhenTheDocumentCarriesIt(string mode)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var receipt = ThermalReceipt() with { OutputMode = mode };

        using var enabled = PdfDocument.Open(new SaleReceiptPdfRenderer().Render(receipt with
        {
            Footer = receipt.Footer with { FooterText = "Please come again", PoweredByText = "Powered by: Irani Mart Technology" }
        }));
        var enabledText = string.Concat(enabled.GetPages().Select(page => page.Text));
        Assert.Contains("Please come again", enabledText);
        Assert.Contains("Powered by: Irani Mart Technology", enabledText);

        using var disabled = PdfDocument.Open(new SaleReceiptPdfRenderer().Render(receipt with
        {
            Footer = receipt.Footer with { FooterText = "Please come again", PoweredByText = null }
        }));
        var disabledText = string.Concat(disabled.GetPages().Select(page => page.Text));
        Assert.Contains("Please come again", disabledText);
        Assert.DoesNotContain("Powered by", disabledText);
    }

    [Fact]
    public void ThermalPdf_LeavesOffUnconfiguredLinesAndKeepsLongReceiptsOnOneRoll()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var receipt = ThermalReceipt(itemCount: 60) with
        {
            Header = new(false, null, "Shop", "Outlet", null, null, null, null),
            Footer = new("Thank You", null, null, false, false, null, DateTime.UtcNow)
        };

        using var pdf = PdfDocument.Open(new SaleReceiptPdfRenderer().Render(receipt));
        var text = Assert.Single(pdf.GetPages()).Text;

        foreach (var hidden in new[] { "A concern of", "Address:", "Hotline", "Web:", "BIN:", "Return / Exchange Policy", "Powered by" })
        {
            Assert.DoesNotContain(hidden, text);
        }
    }
}
