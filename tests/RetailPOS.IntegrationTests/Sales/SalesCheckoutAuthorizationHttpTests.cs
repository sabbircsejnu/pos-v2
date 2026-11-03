using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

/// <summary>HTTP-level checks that checkout identity, scope and replay rules are enforced by the server (P1).</summary>
public sealed class SalesCheckoutAuthorizationHttpTests : IClassFixture<RetailPosApiFactory>
{
    private const long CashierId = 7001;
    private readonly RetailPosApiFactory _factory;

    public SalesCheckoutAuthorizationHttpTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task OmittedCashier_IsTakenFromTheAuthenticatedPrincipal()
    {
        await SeedAsync();
        using var client = CreateClient();
        var request = BuildRequest("claim-cashier");
        request.CashierId = null;

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(CashierId, (await db.Sales.SingleAsync(sale => sale.IdempotencyKey == "claim-cashier")).CashierId);
    }

    [Fact]
    public async Task SubmittedCashierThatDiffersFromThePrincipal_IsForbiddenAndNothingIsSold()
    {
        await SeedAsync();
        using var client = CreateClient();
        var request = BuildRequest("spoofed-cashier");
        request.CashierId = 9999;

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.False(await db.Sales.AnyAsync(sale => sale.IdempotencyKey == "spoofed-cashier"));
        Assert.Equal(50, (await db.Inventories.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task MissingIdempotencyKey_IsRejectedByRequestValidation()
    {
        await SeedAsync();
        using var client = CreateClient();
        var request = BuildRequest("ignored");
        request.IdempotencyKey = null;

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReplayWithTheSameKey_ReturnsTheOriginalSale_AndADifferentPayloadIsAnHttp409()
    {
        await SeedAsync();
        using var client = CreateClient();

        var first = await client.PostAsJsonAsync("/api/sales", BuildRequest("replay-http"));
        var replay = await client.PostAsJsonAsync("/api/sales", BuildRequest("replay-http"));
        var changed = BuildRequest("replay-http");
        changed.Items[0].Quantity = 1;
        changed.Tax = 10m;
        changed.CashTendered = 110m;
        var conflict = await client.PostAsJsonAsync("/api/sales", changed);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(await SaleIdAsync(first), await SaleIdAsync(replay));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var document = await JsonDocument.ParseAsync(await conflict.Content.ReadAsStreamAsync());
        Assert.Equal("IDEMPOTENCY_KEY_PAYLOAD_MISMATCH", document.RootElement.GetProperty("errorCode").GetString());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(1, await db.Sales.CountAsync(sale => sale.IdempotencyKey == "replay-http"));
    }

    [Fact]
    public async Task StalePrice_IsAnHttp409ThatCarriesTheCurrentServerPrice()
    {
        await SeedAsync();
        using var client = CreateClient();
        var request = BuildRequest("stale-http");
        request.Items[0].UnitPrice = 90m;

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("SALE_PRICE_CHANGED", document.RootElement.GetProperty("errorCode").GetString());
        var change = document.RootElement.GetProperty("data")[0];
        Assert.Equal(3201, change.GetProperty("variantId").GetInt64());
        Assert.Equal(100m, change.GetProperty("currentUnitPrice").GetDecimal());
    }

    [Theory]
    [InlineData(5002L)] // another outlet of the same business the cashier is not assigned to
    [InlineData(5003L)] // an outlet of another business
    public async Task ExchangeLink_ToASaleOutsideTheCallersOutletScope_IsForbidden(long originalOutletId)
    {
        await SeedAsync();
        using var client = CreateClient();
        var originalId = originalOutletId == 5002 ? 9002L : 9003L;
        var request = BuildRequest($"exchange-scope-{originalOutletId}");
        request.OriginalSaleId = originalId;

        var response = await client.PostAsJsonAsync("/api/sales", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Null((await db.Sales.SingleAsync(sale => sale.Id == originalId)).ReplacementSaleId);
    }

    private static async Task<long> SaleIdAsync(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("data").GetProperty("id").GetInt64();
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", CashierId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Cashier");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "sales.view,sales.create");
        return client;
    }

    // 2 x 100.00 at the default 10% VAT, paid in cash.
    private static CreateSaleDto BuildRequest(string key) => new()
    {
        OutletId = 5001,
        TerminalId = 8001,
        CashierId = CashierId,
        PaymentMethod = "cash",
        IdempotencyKey = key,
        Tax = 20m,
        CashTendered = 220m,
        Items = [new CreateSaleItemDto { VariantId = 3201, Quantity = 2, UnitPrice = 100m }]
    };

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Business one", IsActive = true },
            new Business { Id = 2, Name = "Business two", IsActive = true });
        db.Outlets.AddRange(
            new Outlet { Id = 5001, BusinessId = 1, Name = "Main", Address = "Dhaka", InvoicePrefix = "MAIN" },
            new Outlet { Id = 5002, BusinessId = 1, Name = "Branch", Address = "Dhaka", InvoicePrefix = "BRN" },
            new Outlet { Id = 5003, BusinessId = 2, Name = "Other business", Address = "Dhaka", InvoicePrefix = "OTH" });
        db.Users.Add(new User
        {
            Id = CashierId, BusinessId = 1, Name = "Cashier", Email = "cashier@example.test", PasswordHash = "x",
            OutletId = 5001, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly, IsActive = true
        });
        db.Customers.Add(new Customer
        {
            Id = 6001, BusinessId = 1, Name = Customer.WalkInCustomerName, CustomerCode = Customer.WalkInCustomerCode,
            IsSystem = true, IsActive = true
        });
        db.Categories.Add(new Category { Id = 3001, BusinessId = 1, Name = "General", Description = "Test" });
        db.Products.Add(new Product
        {
            Id = 3101, BusinessId = 1, Name = "Test Product", ProductCode = "P-3101", CategoryId = 3001, BasePrice = 100m,
            CostPrice = 70m, Status = ProductStatus.Active, HasVariants = true
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 3201, BusinessId = 1, ProductId = 3101, Name = "Default", Sku = "SKU-3201", Barcode = "BC-3201", Attributes = "{}"
        });
        db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
        {
            Id = 9001, VariantId = 3201, LocationType = "outlet", LocationId = 5001, Quantity = 50, LowStockThreshold = 5
        });
        db.PosTerminals.Add(new PosTerminal
        {
            Id = 8001, OutletId = 5001, Name = "Counter-1", Code = "T-1", IsActive = true, IsDefault = true
        });
        db.CashSessions.Add(new CashSession
        {
            Id = 8101, BusinessId = 1, OutletId = 5001, TerminalId = 8001, BusinessDate = DateTime.UtcNow.Date,
            OpenedAt = DateTime.UtcNow, OpeningCashAmount = 1000m, Status = "open", OpenedByUserId = CashierId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        db.Sales.AddRange(
            new Sale { Id = 9002, OutletId = 5002, CashierId = CashierId, Status = "completed", SaleNumber = "BR-1" },
            new Sale { Id = 9003, OutletId = 5003, CashierId = CashierId, Status = "completed", SaleNumber = "OTHER-1" });
        await db.SaveChangesAsync();
    }
}
