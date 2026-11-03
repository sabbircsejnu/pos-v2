using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public sealed class StockLedgerReadHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public StockLedgerReadHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReadRoutes_PreserveResponseShapeAndRestrictAuthorizedLocations()
    {
        await SeedAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "4101");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "4102");
        client.DefaultRequestHeaders.Add("X-Test-Role", "BusinessOwner");
        client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", "BusinessOwner");

        using var byIdResponse = await client.GetAsync("/api/stock-ledger/9101");
        Assert.Equal(HttpStatusCode.OK, byIdResponse.StatusCode);
        using var byIdDocument = JsonDocument.Parse(await byIdResponse.Content.ReadAsStringAsync());
        var byId = byIdDocument.RootElement.GetProperty("data");
        Assert.Equal(9101, byId.GetProperty("id").GetInt64());
        Assert.Equal("Ledger outlet", byId.GetProperty("locationName").GetString());
        Assert.Equal(12.5m, byId.GetProperty("unitCost").GetDecimal());

        using var deniedById = await client.GetAsync("/api/stock-ledger/9102");
        Assert.Equal(HttpStatusCode.NotFound, deniedById.StatusCode);

        using var timelineResponse = await client.GetAsync(
            "/api/stock-ledger/variant/8101/location/5101?locationType=outlet");
        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        using var timelineDocument = JsonDocument.Parse(await timelineResponse.Content.ReadAsStringAsync());
        Assert.Single(timelineDocument.RootElement.GetProperty("data").EnumerateArray());

        using var referenceResponse = await client.GetAsync("/api/stock-ledger/reference/sale/99");
        Assert.Equal(HttpStatusCode.OK, referenceResponse.StatusCode);
        using var referenceDocument = JsonDocument.Parse(await referenceResponse.Content.ReadAsStringAsync());
        Assert.Single(referenceDocument.RootElement.GetProperty("data").EnumerateArray());

        using var searchResponse = await client.PostAsJsonAsync(
            "/api/stock-ledger/search",
            new { pageNumber = 1, pageSize = 20 });
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        using var searchDocument = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
        var result = searchDocument.RootElement.GetProperty("data");
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(9101, Assert.Single(result.GetProperty("entries").EnumerateArray())
            .GetProperty("id").GetInt64());
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Businesses.AddRange(
            new Business { Id = 4101, Name = "Authorized business" },
            new Business { Id = 4201, Name = "Foreign business" });
        dbContext.Categories.Add(new Category { Id = 6101, BusinessId = 4101, Name = "Ledger category" });
        dbContext.Products.Add(new Product
        {
            Id = 7101,
            BusinessId = 4101,
            Name = "Ledger product",
            CategoryId = 6101
        });
        dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = 8101,
            BusinessId = 4101,
            ProductId = 7101,
            Name = "Default",
            Sku = "LEDGER-1"
        });
        dbContext.Outlets.AddRange(
            new Outlet { Id = 5101, BusinessId = 4101, Name = "Ledger outlet" },
            new Outlet { Id = 5102, BusinessId = 4101, Name = "Unassigned outlet" },
            new Outlet { Id = 5201, BusinessId = 4201, Name = "Foreign outlet" });
        dbContext.Users.Add(new User
        {
            Id = 4102,
            BusinessId = 4101,
            OutletId = 5101,
            Name = "Ledger reader",
            Email = "ledger-reader@example.test",
            PasswordHash = "test",
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly
        });
        await dbContext.SaveChangesAsync();
        dbContext.StockLedgers.AddRange(
            CreateEntry(9101, 5101),
            CreateEntry(9102, 5102),
            CreateEntry(9103, 5201));
        await dbContext.SaveChangesAsync();
    }

    private static StockLedger CreateEntry(long id, long locationId) =>
        new()
        {
            Id = id,
            VariantId = 8101,
            LocationId = locationId,
            LocationType = "outlet",
            TransactionType = "sale",
            QtyOut = 1,
            BalanceAfter = 3,
            UnitCost = 12.5m,
            MovementValue = 12.5m,
            IsCostValuationReliable = true,
            BusinessDate = new DateTime(2026, 10, 5),
            MovementSequence = id,
            ReferenceType = "sale",
            ReferenceId = 99,
            CreatedAt = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc)
        };
}
