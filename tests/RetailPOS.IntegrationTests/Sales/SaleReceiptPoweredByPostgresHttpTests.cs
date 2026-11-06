using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using UglyToad.PdfPig;

namespace RetailPOS.Tests.Sales;

/// <summary>
/// The whole Powered By path through the real HTTP pipeline and PostgreSQL (isolated Test stack only):
/// Super Admin save → persisted Business settings → receipt document JSON → receipt PDF.
/// </summary>
public sealed class SaleReceiptPoweredByPostgresHttpTests
{
    private const string Text = "Powered by: Irani Mart Technology";

    private static HttpClient Client(MobileSalesDashboardPostgresApiFactory factory, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "5");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "*");
        return client;
    }

    private static StringContent Footer(bool show) =>
        new($$"""{"showPoweredByOnReceipt":{{(show ? "true" : "false")}},"poweredByText":"{{Text}}"}""", Encoding.UTF8, "application/json");

    private static async Task<string?> DocumentPoweredByAsync(HttpClient client, string mode)
    {
        using var response = await client.GetAsync($"/api/sales/101/receipt-document?mode={mode}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("footer").GetProperty("poweredByText").GetString();
    }

    private static async Task<string> PdfTextAsync(HttpClient client, string mode)
    {
        using var response = await client.GetAsync($"/api/sales/101/receipt-pdf?mode={mode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return string.Concat(pdf.GetPages().Select(page => page.Text));
    }

    [Fact]
    public Task SavedSettingControlsTheReceiptJsonAndPdf() => PostgresTestDatabase.RunAsync(
        InfrastructureTestSettings.RequireIsolatedPostgres(),
        PostgresTestDatabasePrefix.SaleCreation,
        PostgresTestDatabaseStrategy.TemplateClone,
        async database =>
        {
            QuestPDF.Settings.License = LicenseType.Community;
            await using var factory = new MobileSalesDashboardPostgresApiFactory(database.ConnectionString);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                await db.Database.MigrateAsync();
                db.Businesses.Add(new Business { Id = 1, Name = "One", IsActive = true });
                await db.SaveChangesAsync();
                db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Main", Address = "Dhaka", InvoicePrefix = "DHK" });
                db.Users.Add(new User { Id = 5, BusinessId = 1, Name = "Cashier", Email = "c@example.test", PasswordHash = "x", IsActive = true });
                await db.SaveChangesAsync();
                db.Sales.Add(new Sale
                {
                    Id = 101,
                    SaleNumber = "DHK2611051",
                    BusinessId = 1,
                    OutletId = 11,
                    CashierId = 5,
                    SaleDate = new DateTime(2026, 11, 5, 10, 20, 0, DateTimeKind.Utc),
                    TotalAmount = 100m,
                    PaymentMethod = "cash",
                    Status = "completed"
                });
                await db.SaveChangesAsync();
            }

            using var admin = Client(factory, "Super Admin");
            using var reader = Client(factory, "BusinessOwner");

            // Out of the box: nothing prints.
            Assert.Null(await DocumentPoweredByAsync(reader, "80mm"));

            using (var on = await admin.PatchAsync("/api/businesses/1/receipt-footer", Footer(true)))
                Assert.Equal(HttpStatusCode.OK, on.StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var saved = await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
                    .Businesses.AsNoTracking().SingleAsync(item => item.Id == 1);
                Assert.True(saved.ShowPoweredByOnReceipt);
                Assert.Equal(Text, saved.PoweredByText);
            }

            Assert.Equal(Text, await DocumentPoweredByAsync(reader, "80mm"));
            Assert.Equal(Text, await DocumentPoweredByAsync(reader, "A4"));
            var thermal = await PdfTextAsync(reader, "80mm");
            Assert.EndsWith(Text, thermal.TrimEnd());
            Assert.Contains(Text, await PdfTextAsync(reader, "A4"));

            using (var off = await admin.PatchAsync("/api/businesses/1/receipt-footer", Footer(false)))
                Assert.Equal(HttpStatusCode.OK, off.StatusCode);

            Assert.Null(await DocumentPoweredByAsync(reader, "80mm"));
            Assert.DoesNotContain("Powered by", await PdfTextAsync(reader, "80mm"));
            Assert.DoesNotContain("Powered by", await PdfTextAsync(reader, "A4"));

            // Still Super Admin only through this pipeline.
            using var denied = await reader.PatchAsync("/api/businesses/1/receipt-footer", Footer(true));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Null(await DocumentPoweredByAsync(reader, "80mm"));
        });
}
