using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

/// <summary>The Stock Movement and Product Ledger reports use the Company day and stay inside the Business.</summary>
public sealed class StockReportCompanyDayHttpTests : IClassFixture<RetailPosApiFactory>
{
    private const long BusinessId = 4401;
    private const long ForeignBusinessId = 4402;
    private const long ProductId = 7401;
    private const long ForeignProductId = 7402;
    private const long VariantId = 8401;
    private const long Outlet = 5401;

    private readonly RetailPosApiFactory _factory;

    public StockReportCompanyDayHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProductLedger_IncludesExactlyTheCompanyDay()
    {
        await SeedAsync();
        using var client = CreateClient();

        var report = await GetDataAsync(
            client,
            $"/api/reports/inventory/product-ledger?productId={ProductId}&outletId={Outlet}&dateFrom=2026-11-05&dateTo=2026-11-05");

        var summary = report.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("openingStock").GetInt32());
        Assert.Equal(6, summary.GetProperty("totalStockIn").GetInt32());
        Assert.Equal(0, summary.GetProperty("totalStockOut").GetInt32());
        Assert.Equal(7, summary.GetProperty("closingStock").GetInt32());
        Assert.Equal(2, report.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task StockMovement_IncludesExactlyTheCompanyDay()
    {
        await SeedAsync();
        using var client = CreateClient();

        var report = await GetDataAsync(
            client,
            $"/api/reports/inventory/stock-movement?outletId={Outlet}&dateFrom=2026-11-05&dateTo=2026-11-05&page=1&pageSize=20");

        var row = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(1, row.GetProperty("openingStock").GetInt32());
        Assert.Equal(6, row.GetProperty("stockIn").GetInt32());
        Assert.Equal(7, row.GetProperty("closingStock").GetInt32());
    }

    [Fact]
    public async Task Exports_UseTheSameCompanyDayWindow()
    {
        await SeedAsync();
        using var client = CreateClient();

        using var ledger = await client.GetAsync(
            $"/api/reports/inventory/product-ledger/export?productId={ProductId}&outletId={Outlet}&dateFrom=2026-11-06&dateTo=2026-11-06&format=csv");

        Assert.Equal(HttpStatusCode.OK, ledger.StatusCode);
        var csv = await ledger.Content.ReadAsStringAsync();
        Assert.Contains("GRN-24", csv);
        Assert.DoesNotContain("GRN-23", csv);
    }

    [Fact]
    public async Task ProductLedger_RefusesAnotherBusinessesProduct()
    {
        await SeedAsync();
        using var client = CreateClient();

        using var report = await client.GetAsync(
            $"/api/reports/inventory/product-ledger?productId={ForeignProductId}");
        using var export = await client.GetAsync(
            $"/api/reports/inventory/product-ledger/export?productId={ForeignProductId}&format=csv");

        Assert.Equal(HttpStatusCode.NotFound, report.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, export.StatusCode);
        Assert.DoesNotContain("Foreign product", await report.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} returned {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "reports.inventory");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", BusinessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", "4411");
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

        db.Businesses.AddRange(
            new Business { Id = BusinessId, Name = "Report business" },
            new Business { Id = ForeignBusinessId, Name = "Foreign business" });
        db.BusinessPosSettings.Add(new BusinessPosSettings { BusinessId = BusinessId, DefaultTimeZoneId = "Asia/Dhaka" });
        db.Categories.AddRange(
            new Category { Id = 6401, BusinessId = BusinessId, Name = "Report category", Description = "" },
            new Category { Id = 6402, BusinessId = ForeignBusinessId, Name = "Foreign category", Description = "" });
        db.Products.AddRange(
            new Product { Id = ProductId, BusinessId = BusinessId, Name = "Report product", CategoryId = 6401 },
            new Product { Id = ForeignProductId, BusinessId = ForeignBusinessId, Name = "Foreign product", CategoryId = 6402 });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = VariantId,
            BusinessId = BusinessId,
            ProductId = ProductId,
            Name = "Default",
            Sku = "DAY-1"
        });
        db.Outlets.Add(new Outlet { Id = Outlet, BusinessId = BusinessId, Name = "Report outlet" });
        db.Users.Add(new User
        {
            Id = 4411,
            BusinessId = BusinessId,
            OutletId = Outlet,
            Name = "Owner",
            Email = "day-owner@example.test",
            PasswordHash = "test-only",
            InventoryLocationAccessScope = User.InventoryAccessAll,
            IsActive = true
        });
        db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
        {
            VariantId = VariantId,
            LocationId = Outlet,
            LocationType = "outlet",
            Quantity = 15
        });

        // Dhaka is UTC+6, so 5 November there runs from 18:00Z on the 4th to 18:00Z on the 5th.
        db.StockLedgers.AddRange(
            Receipt(6401, 21, 1, 1, new DateTime(2026, 11, 4, 17, 59, 59, 999, DateTimeKind.Utc)),
            Receipt(6402, 22, 2, 3, new DateTime(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc)),
            Receipt(6403, 23, 4, 7, new DateTime(2026, 11, 5, 17, 59, 59, 999, DateTimeKind.Utc)),
            Receipt(6404, 24, 8, 15, new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
    }

    private static StockLedger Receipt(long id, long referenceId, int quantity, int balance, DateTime createdAt) =>
        new()
        {
            Id = id,
            VariantId = VariantId,
            LocationId = Outlet,
            LocationType = "outlet",
            TransactionType = StockLedgerTransactionType.Grn,
            QtyIn = quantity,
            BalanceAfter = balance,
            ReferenceType = StockLedgerReferenceType.Grn,
            ReferenceId = referenceId,
            CreatedAt = createdAt
        };
}
