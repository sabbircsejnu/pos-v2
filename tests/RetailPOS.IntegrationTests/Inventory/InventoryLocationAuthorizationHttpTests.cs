using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RetailPOS.Application.Authentication.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public sealed class InventoryLocationAuthorizationHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public InventoryLocationAuthorizationHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExplicitOutletAndWarehouseReads_AllowAssignedLocationsOnly()
    {
        await ResetAndSeedAsync(_factory);
        using var client = CreateClient(_factory);

        using var outletResponse = await client.GetAsync("/api/inventory/outlet/5001");
        using var warehouseResponse = await client.GetAsync("/api/inventory/warehouse/2001");

        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, warehouseResponse.StatusCode);
        using var outletDocument = JsonDocument.Parse(await outletResponse.Content.ReadAsStringAsync());
        using var warehouseDocument = JsonDocument.Parse(await warehouseResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, Assert.Single(outletDocument.RootElement.GetProperty("data").EnumerateArray())
            .GetProperty("id").GetInt64());
        Assert.Equal(3, Assert.Single(warehouseDocument.RootElement.GetProperty("data").EnumerateArray())
            .GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task OmittedFilters_RestrictEveryInventoryReadToAuthorizedLocations()
    {
        await ResetAndSeedAsync(_factory);
        using var client = CreateClient(_factory);

        using var lowStockResponse = await client.GetAsync("/api/inventory/low-stock");
        using var outOfStockResponse = await client.GetAsync("/api/inventory/out-of-stock");
        using var expiringResponse = await client.GetAsync("/api/inventory/expiring-soon");
        using var valuationResponse = await client.GetAsync("/api/inventory/valuation");
        using var searchResponse = await client.PostAsJsonAsync("/api/inventory/search", new { page = 1, pageSize = 10 });
        using var filtersResponse = await client.GetAsync("/api/inventory/variant-attribute-filters");

        Assert.Equal(HttpStatusCode.OK, lowStockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, outOfStockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, expiringResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valuationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, filtersResponse.StatusCode);

        using var lowStock = JsonDocument.Parse(await lowStockResponse.Content.ReadAsStringAsync());
        using var outOfStock = JsonDocument.Parse(await outOfStockResponse.Content.ReadAsStringAsync());
        using var expiring = JsonDocument.Parse(await expiringResponse.Content.ReadAsStringAsync());
        using var valuation = JsonDocument.Parse(await valuationResponse.Content.ReadAsStringAsync());
        using var search = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
        using var filters = JsonDocument.Parse(await filtersResponse.Content.ReadAsStringAsync());

        Assert.Equal(new long[] { 1, 2, 3 }, lowStock.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("inventoryId").GetInt64()).Order());
        Assert.Equal(new long[] { 1 }, outOfStock.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64()).Order());
        Assert.Equal(new long[] { 2, 3 }, expiring.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64()).Order());
        Assert.Equal(3, valuation.RootElement.GetProperty("data").GetProperty("totalItems").GetInt32());
        Assert.Equal(5, valuation.RootElement.GetProperty("data").GetProperty("totalQuantity").GetInt32());
        Assert.Equal(new long[] { 1, 2, 3 }, search.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64()).Order());

        var colorFilter = Assert.Single(filters.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(new[] { "A1", "A2", "W1" }, colorFilter.GetProperty("values").EnumerateArray()
            .Select(value => value.GetString()).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("SECRET-A3", colorFilter.GetProperty("values").EnumerateArray()
            .Select(value => value.GetString()));
        Assert.DoesNotContain("SECRET-B1", colorFilter.GetProperty("values").EnumerateArray()
            .Select(value => value.GetString()));
    }

    [Fact]
    public async Task UnauthorizedLocationsAndFilters_AreRejectedBeforeInventoryQueries()
    {
        using var factory = new NoQueryDispatchRetailPosApiFactory();
        await ResetAndSeedAsync(factory);
        using var client = CreateClient(factory);

        var responses = new List<HttpResponseMessage>
        {
            await client.GetAsync("/api/inventory/outlet/5003"),
            await client.GetAsync("/api/inventory/outlet/6001"),
            await client.GetAsync("/api/inventory/warehouse/2002"),
            await client.GetAsync("/api/inventory/warehouse/6002"),
            await client.GetAsync("/api/inventory/outlet/2001"),
            await client.GetAsync("/api/inventory/warehouse/5001"),
            await client.GetAsync("/api/inventory/outlet/999999"),
            await client.GetAsync("/api/inventory/low-stock?outletId=5003"),
            await client.GetAsync("/api/inventory/out-of-stock?warehouseId=6002"),
            await client.GetAsync("/api/inventory/expiring-soon?outletId=6001"),
            await client.GetAsync("/api/inventory/valuation?warehouseId=2002"),
            await client.PostAsJsonAsync("/api/inventory/search", new { outletId = 6001 }),
            await client.GetAsync("/api/inventory/variant-attribute-filters?warehouseId=6002")
        };

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
            factory.SenderMock.VerifyNoOtherCalls();
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task SuperAdminWithoutValidatedBusinessContext_IsDeniedBeforeInventoryQuery()
    {
        using var factory = new NoQueryDispatchRetailPosApiFactory();
        await ResetAndSeedAsync(factory);
        using var client = CreateClient(
            factory,
            userId: 999,
            role: "Super Admin",
            businessId: 1,
            permissions: "inventory.view");

        using var response = await client.GetAsync("/api/inventory/valuation");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.SenderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SuperAdminWithBusinessContext_CannotReadAnotherBusinessesOutlet()
    {
        using var factory = new NoQueryDispatchRetailPosApiFactory();
        await ResetAndSeedAsync(factory);
        factory.SenderMock
            .Setup(sender => sender.Send(
                It.IsAny<IRequest<BusinessContextResult>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessContextResult(1, "Business A"));
        using var client = CreateClient(
            factory,
            userId: 999,
            role: "Super Admin",
            businessId: 1,
            permissions: "inventory.view");
        client.DefaultRequestHeaders.Add("X-Business-Id", "1");

        using var response = await client.GetAsync("/api/inventory/outlet/6001");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.SenderMock.Verify(
            sender => sender.Send(
                It.IsAny<IRequest<BusinessContextResult>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        factory.SenderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LowStockRoute_StillRequiresItsExistingPermission()
    {
        await ResetAndSeedAsync(_factory);
        using var client = CreateClient(_factory, permissions: "inventory.view");

        using var response = await client.GetAsync("/api/inventory/low-stock");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static HttpClient CreateClient(
        RetailPosApiFactory factory,
        long userId = 777,
        string role = "InventoryAdmin",
        long businessId = 1,
        string permissions = "inventory.view,low_stock_alerts.view")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private static async Task ResetAndSeedAsync(RetailPosApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Business A" },
            new Business { Id = 2, Name = "Business B" });
        db.Outlets.AddRange(
            new Outlet { Id = 5001, BusinessId = 1, Name = "A1" },
            new Outlet { Id = 5002, BusinessId = 1, Name = "A2" },
            new Outlet { Id = 5003, BusinessId = 1, Name = "A3" },
            new Outlet { Id = 6001, BusinessId = 2, Name = "B1" });
        db.Warehouses.AddRange(
            new Warehouse { Id = 2001, BusinessId = 1, Name = "A Warehouse 1" },
            new Warehouse { Id = 2002, BusinessId = 1, Name = "A Warehouse 2" },
            new Warehouse { Id = 6002, BusinessId = 2, Name = "B Warehouse" });
        db.Users.Add(new User
        {
            Id = 777,
            BusinessId = 1,
            OutletId = 5001,
            Name = "Assigned inventory user",
            Email = "inventory-location-test@example.com",
            PasswordHash = "x",
            InventoryLocationAccessScope = User.InventoryAccessSpecific,
            IsActive = true
        });
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { Id = 1, UserId = 777, OutletId = 5001, BusinessId = 1, IsPrimary = true },
            new UserOutletAssignment { Id = 2, UserId = 777, OutletId = 5002, BusinessId = 1 });
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment
        {
            Id = 1,
            UserId = 777,
            WarehouseId = 2001,
            BusinessId = 1,
            IsPrimary = true
        });
        db.Categories.Add(new Category { Id = 7001, Name = "Authorization test", BusinessId = 1 });
        db.Products.Add(new Product
        {
            Id = 8001,
            BusinessId = 1,
            Name = "Authorization test product",
            ProductCode = "AUTH-001",
            CategoryId = 7001,
            BasePrice = 10m,
            CostPrice = 5m
        });
        db.Variations.Add(new Variation { Id = 7101, Name = "Color", DisplayOrder = 1, IsActive = true });
        var locations = new (long VariantId, long LocationId, string LocationType, string Attribute, int Quantity)[]
        {
            (9001, 5001, "outlet", "A1", 0),
            (9002, 5002, "outlet", "A2", 2),
            (9003, 2001, "warehouse", "W1", 3),
            (9004, 5003, "outlet", "SECRET-A3", 4),
            (9005, 6001, "outlet", "SECRET-B1", 5),
            (9006, 2002, "warehouse", "SECRET-A2-W", 6),
            (9007, 6002, "warehouse", "SECRET-B-W", 7)
        };
        foreach (var (variantId, _, _, attribute, _) in locations)
        {
            db.ProductVariants.Add(new ProductVariant
            {
                Id = variantId,
                BusinessId = 1,
                ProductId = 8001,
                Name = attribute,
                Sku = $"AUTH-{variantId}",
                Attributes = JsonSerializer.Serialize(new { Color = attribute })
            });
            db.VariationOptions.Add(new VariationOption
            {
                Id = 7200 + variantId - 9000,
                VariationId = 7101,
                Name = attribute,
                DisplayOrder = (int)(variantId - 9000),
                IsActive = true
            });
        }

        foreach (var (variantId, locationId, locationType, _, quantity) in locations)
        {
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = variantId - 9000,
                VariantId = variantId,
                LocationId = locationId,
                LocationType = locationType,
                Quantity = quantity,
                LowStockThreshold = 10,
                ExpiryDate = DateTime.UtcNow.AddDays(15)
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed class NoQueryDispatchRetailPosApiFactory : RetailPosApiFactory
    {
        public Mock<ISender> SenderMock { get; } = new(MockBehavior.Strict);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton(SenderMock.Object);
            });
        }
    }
}
