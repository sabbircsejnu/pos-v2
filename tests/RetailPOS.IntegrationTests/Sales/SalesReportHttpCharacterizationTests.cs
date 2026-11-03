using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

public sealed class SalesReportHttpCharacterizationTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public SalesReportHttpCharacterizationTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SalesReports_PreserveRouteResponseAndAggregationContracts()
    {
        await SeedAsync();
        using var client = CreateBusinessOwnerClient();

        using var summaryResponse = await client.GetAsync(
            "/api/reports/sales/summary?startDate=2026-09-01T00%3A00%3A00Z&endDate=2026-09-30T23%3A59%3A59Z&outletId=5101&paymentMethod=cash");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        using var summaryDocument = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
        var summary = summaryDocument.RootElement.GetProperty("data");
        Assert.Equal(100m, summary.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(5m, summary.GetProperty("totalDiscount").GetDecimal());
        Assert.Equal(10m, summary.GetProperty("totalTax").GetDecimal());
        Assert.Equal(95m, summary.GetProperty("netRevenue").GetDecimal());
        Assert.Equal(1, summary.GetProperty("totalTransactions").GetInt32());
        Assert.Equal(95m, summary.GetProperty("averageOrderValue").GetDecimal());

        using var topProductsResponse = await client.GetAsync(
            "/api/reports/sales/top-products?startDate=2026-09-01T00%3A00%3A00Z&endDate=2026-09-30T23%3A59%3A59Z&outletId=5101&limit=1");
        Assert.Equal(HttpStatusCode.OK, topProductsResponse.StatusCode);
        using var topProductsDocument = JsonDocument.Parse(await topProductsResponse.Content.ReadAsStringAsync());
        var topProduct = Assert.Single(topProductsDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(8101, topProduct.GetProperty("variantId").GetInt64());
        Assert.Equal("Report product", topProduct.GetProperty("productName").GetString());
        Assert.Equal("REPORT-1", topProduct.GetProperty("sku").GetString());
        Assert.Equal(2, topProduct.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(100m, topProduct.GetProperty("totalRevenue").GetDecimal());

        using var byOutletResponse = await client.GetAsync(
            "/api/reports/sales/by-outlet?startDate=2026-09-01T00%3A00%3A00Z&endDate=2026-09-30T23%3A59%3A59Z");
        Assert.Equal(HttpStatusCode.OK, byOutletResponse.StatusCode);
        using var byOutletDocument = JsonDocument.Parse(await byOutletResponse.Content.ReadAsStringAsync());
        var outlet = Assert.Single(byOutletDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(5101, outlet.GetProperty("outletId").GetInt64());
        Assert.Equal("Report outlet", outlet.GetProperty("outletName").GetString());
        Assert.Equal(1, outlet.GetProperty("transactionCount").GetInt32());
        Assert.Equal(100m, outlet.GetProperty("totalRevenue").GetDecimal());

        using var byPaymentResponse = await client.GetAsync(
            "/api/reports/sales/by-payment-method?startDate=2026-09-01T00%3A00%3A00Z&endDate=2026-09-30T23%3A59%3A59Z&outletId=5101");
        Assert.True(
            byPaymentResponse.StatusCode == HttpStatusCode.OK,
            await byPaymentResponse.Content.ReadAsStringAsync());
        using var byPaymentDocument = JsonDocument.Parse(await byPaymentResponse.Content.ReadAsStringAsync());
        var payment = Assert.Single(byPaymentDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("cash", payment.GetProperty("paymentMethod").GetString());
        Assert.Equal(1, payment.GetProperty("count").GetInt32());
        Assert.Equal(100m, payment.GetProperty("amount").GetDecimal());

        using var trendResponse = await client.GetAsync(
            "/api/reports/sales/daily-trend?startDate=2026-09-01T00%3A00%3A00Z&endDate=2026-09-30T23%3A59%3A59Z&outletId=5101");
        Assert.Equal(HttpStatusCode.OK, trendResponse.StatusCode);
        using var trendDocument = JsonDocument.Parse(await trendResponse.Content.ReadAsStringAsync());
        var trend = Assert.Single(trendDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("2026-09-15T00:00:00Z", trend.GetProperty("date").GetString());
        Assert.Equal(1, trend.GetProperty("transactionCount").GetInt32());
        Assert.Equal(100m, trend.GetProperty("revenue").GetDecimal());
    }

    [Fact]
    public async Task ByOutletReport_ExcludesSalesOutsideTrustedBusinessOutletScope()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Businesses.Add(new Business { Id = 4201, Name = "Foreign business" });
            db.Outlets.Add(new Outlet { Id = 5201, BusinessId = 4201, Name = "Foreign outlet" });
            db.Sales.Add(new Sale
            {
                Id = 9201,
                SaleNumber = "S-FOREIGN-1",
                OutletId = 5201,
                CashierId = 4102,
                SaleDate = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
                Status = "completed",
                PaymentMethod = "cash",
                TotalAmount = 900m
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateBusinessOwnerClient();
        using var response = await client.GetAsync(
            "/api/reports/sales/by-outlet?startDate=2026-09-01T00%3A00%3A00Z&endDate=2026-09-30T23%3A59%3A59Z");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var outlet = Assert.Single(document.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(5101, outlet.GetProperty("outletId").GetInt64());
        Assert.Equal(100m, outlet.GetProperty("totalRevenue").GetDecimal());
    }

    private HttpClient CreateBusinessOwnerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "reports.sales");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "4101");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "4102");
        client.DefaultRequestHeaders.Add("X-Test-Role", "BusinessOwner");
        client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", "BusinessOwner");
        return client;
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 4101, Name = "Report business" });
        db.Categories.Add(new Category { Id = 6101, BusinessId = 4101, Name = "Report category", Description = "" });
        db.Outlets.Add(new Outlet { Id = 5101, BusinessId = 4101, Name = "Report outlet" });
        db.Users.Add(new User
        {
            Id = 4102,
            BusinessId = 4101,
            OutletId = 5101,
            Name = "Report cashier",
            Email = "report-cashier@example.test",
            PasswordHash = "test",
            IsActive = true
        });
        db.Products.Add(new Product
        {
            Id = 7101,
            BusinessId = 4101,
            Name = "Report product",
            CategoryId = 6101,
            CostPrice = 20m
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 8101,
            BusinessId = 4101,
            ProductId = 7101,
            Name = "Default",
            Sku = "REPORT-1"
        });
        db.Sales.Add(new Sale
        {
            Id = 9101,
            SaleNumber = "S-REPORT-1",
            OutletId = 5101,
            CashierId = 4102,
            SaleDate = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            Status = "completed",
            PaymentMethod = "cash",
            TotalAmount = 100m,
            Discount = 5m,
            Tax = 10m,
            Items =
            [
                new SaleItem
                {
                    Id = 10101,
                    VariantId = 8101,
                    Quantity = 2,
                    UnitPrice = 50m,
                    Subtotal = 100m
                }
            ]
        });
        await db.SaveChangesAsync();
    }
}
