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
}
