using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RetailPOS.API.DTOs.Grn;
using RetailPOS.API.DTOs.PurchaseOrder;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Purchase;

/// <summary>
/// End-to-end coverage for purchasing directly into an Outlet or a Warehouse across
/// Outlet-only, Warehouse-only and mixed-location Businesses.
/// </summary>
public sealed class PurchaseLocationDestinationHttpTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    private const long BusinessId = 1;
    private const long OtherBusinessId = 2;
    private const long OutletId = 1201;
    private const long WarehouseId = 1101;
    private const long OtherBusinessOutletId = 2201;
    private const long SupplierId = 1001;
    private const long VariantId = 1301;
    private const long UserId = 321;

    private const string AllPermissions =
        "purchases.view,purchases.create,purchases.approve,purchases.receive,purchases.edit," +
        "grn.view,grn.create,grn.receive";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task OutletOnlyBusiness_PurchaseAndReceive_StocksOutletWithoutAnyWarehouse()
    {
        using var app = CreateApp();
        await SeedAsync(app.Services, withOutlet: true, withWarehouse: false);
        using var client = CreateClient(app);

        var response = await client.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            BuildPurchaseAndReceive("outlet-only-key", "outlet", OutletId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadApiData<PurchaseAndReceiveResultDto>(response);
        Assert.NotNull(result);
        Assert.Equal("outlet", result!.PurchaseOrder.LocationType);
        Assert.Equal(OutletId, result.PurchaseOrder.LocationId);
        Assert.Equal("Main Outlet", result.PurchaseOrder.LocationName);
        Assert.Equal(OutletId, result.PurchaseOrder.OutletId);
        Assert.Null(result.PurchaseOrder.WarehouseId);
        Assert.Equal("completed", result.PurchaseOrder.Status, ignoreCase: true);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.Warehouses.ToListAsync());
            var order = await db.PurchaseOrders.SingleAsync();
            Assert.Equal(OutletId, order.OutletId);
            Assert.Null(order.WarehouseId);
            var inventory = await db.Inventories.SingleAsync();
            Assert.Equal("outlet", inventory.LocationType);
            Assert.Equal(OutletId, inventory.LocationId);
            Assert.Equal(3, inventory.Quantity);
            var ledger = await db.StockLedgers.SingleAsync();
            Assert.Equal("outlet", ledger.LocationType);
            Assert.Equal(OutletId, ledger.LocationId);
            Assert.Equal(result.GrnId, ledger.ReferenceId);
        }

        var grn = await ReadApiData<GrnDto>(await client.GetAsync($"/api/grns/{result.GrnId}"));
        Assert.Equal("outlet", grn!.LocationType);
        Assert.Equal("Main Outlet", grn.LocationName);
        Assert.Null(grn.WarehouseId);

        var duplicate = await ReadApiData<PurchaseAndReceiveResultDto>(await client.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            BuildPurchaseAndReceive("outlet-only-key", "outlet", OutletId, includeItems: false)));
        Assert.True(duplicate!.IsDuplicateRequest);
        Assert.Equal(result.GrnId, duplicate.GrnId);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(3, await db.Inventories.Select(item => item.Quantity).SingleAsync());
            Assert.Single(await db.StockLedgers.ToListAsync());
        }
    }

    [Fact]
    public async Task OutletOnlyBusiness_ApprovalKeepsOutletDestinationForReceiving()
    {
        using var app = CreateApp();
        await SeedAsync(app.Services, withOutlet: true, withWarehouse: false);
        using var client = CreateClient(app);

        var created = await ReadApiData<PurchaseOrderDto>(await client.PostAsJsonAsync(
            "/api/purchase-orders",
            BuildCreate("outlet", OutletId, quantity: 5)));
        Assert.NotNull(created);
        Assert.Equal("outlet", created!.LocationType);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/purchase-orders/{created.Id}/submit", null)).StatusCode);
        var approved = await ReadApiData<PurchaseOrderDto>(
            await client.PostAsync($"/api/purchase-orders/{created.Id}/approve", null));
        Assert.Equal("approved", approved!.Status, ignoreCase: true);
        Assert.Equal("outlet", approved.LocationType);
        Assert.Equal(OutletId, approved.LocationId);

        var pending = await ReadApiData<List<PurchaseOrderForGrnDto>>(await client.GetAsync("/api/grns/pending-pos"));
        var pendingOrder = Assert.Single(pending!);
        Assert.Equal("outlet", pendingOrder.LocationType);
        Assert.Equal("Main Outlet", pendingOrder.LocationName);

        // Manual (partial) GRN receiving into the outlet is covered against PostgreSQL in
        // PurchaseLocationDestinationRelationalTests; the InMemory provider cannot translate its GroupBy.
        var details = await ReadApiData<PurchaseOrderDto>(await client.GetAsync($"/api/purchase-orders/{created.Id}"));
        Assert.Equal("outlet", details!.LocationType);
        Assert.Equal("Main Outlet", details.LocationName);
    }

    [Fact]
    public async Task WarehouseOnlyBusiness_LegacyWarehouseIdRequestsStillStockWarehouse()
    {
        using var app = CreateApp();
        await SeedAsync(app.Services, withOutlet: false, withWarehouse: true);
        using var client = CreateClient(app);

        var legacyCreate = BuildCreate(null, null, quantity: 4);
        legacyCreate.WarehouseId = WarehouseId;
        var created = await ReadApiData<PurchaseOrderDto>(
            await client.PostAsJsonAsync("/api/purchase-orders", legacyCreate));
        Assert.Equal("warehouse", created!.LocationType);
        Assert.Equal(WarehouseId, created.WarehouseId);
        Assert.Equal("Central Warehouse", created.LocationName);

        var legacyReceive = BuildPurchaseAndReceive("warehouse-only-key", null, null);
        legacyReceive.WarehouseId = WarehouseId;
        var response = await client.PostAsJsonAsync("/api/purchase-orders/purchase-receive", legacyReceive);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = await db.Inventories.SingleAsync();
        Assert.Equal("warehouse", inventory.LocationType);
        Assert.Equal(WarehouseId, inventory.LocationId);
        Assert.Equal(3, inventory.Quantity);
        Assert.All(await db.PurchaseOrders.ToListAsync(), order =>
        {
            Assert.Equal(WarehouseId, order.WarehouseId);
            Assert.Null(order.OutletId);
        });
    }

    [Fact]
    public async Task MixedBusiness_KeepsLocationSpecificStockAndFiltersByLocation()
    {
        using var app = CreateApp();
        await SeedAsync(app.Services, withOutlet: true, withWarehouse: true);
        using var client = CreateClient(app);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            BuildPurchaseAndReceive("mixed-outlet", "outlet", OutletId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            BuildPurchaseAndReceive("mixed-warehouse", "warehouse", WarehouseId))).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var inventories = await db.Inventories.OrderBy(item => item.LocationType).ToListAsync();
            Assert.Equal(2, inventories.Count);
            Assert.All(inventories, item =>
            {
                Assert.Equal(VariantId, item.VariantId);
                Assert.Equal(3, item.Quantity);
            });
            Assert.Equal(("outlet", OutletId), (inventories[0].LocationType, inventories[0].LocationId));
            Assert.Equal(("warehouse", WarehouseId), (inventories[1].LocationType, inventories[1].LocationId));
        }

        var outletOrders = await ReadApiData<List<PurchaseOrderDto>>(
            await client.GetAsync($"/api/purchase-orders?locationType=outlet&locationId={OutletId}"));
        var outletOrder = Assert.Single(outletOrders!);
        Assert.Equal("outlet", outletOrder.LocationType);

        var warehouseSearch = await ReadApiData<PurchaseOrderListDto>(await client.PostAsJsonAsync(
            "/api/purchase-orders/search",
            new PurchaseOrderSearchDto { LocationType = "warehouse", PageNumber = 1, PageSize = 10 }));
        Assert.Equal("warehouse", Assert.Single(warehouseSearch!.PurchaseOrders).LocationType);

        var legacyWarehouseFilter = await ReadApiData<List<PurchaseOrderDto>>(
            await client.GetAsync($"/api/purchase-orders?warehouseId={WarehouseId}"));
        Assert.Equal(WarehouseId, Assert.Single(legacyWarehouseFilter!).WarehouseId);
    }

    [Fact]
    public async Task Destination_EnforcesBusinessAndUserLocationAccess()
    {
        using var app = CreateApp();
        await SeedAsync(
            app.Services,
            withOutlet: true,
            withWarehouse: true,
            accessScope: User.InventoryAccessAssignedOnly,
            assignedOutletId: OutletId,
            step5Warehouse: false);
        using var client = CreateClient(app);

        var otherBusinessOutlet = await client.PostAsJsonAsync(
            "/api/purchase-orders",
            BuildCreate("outlet", OtherBusinessOutletId, quantity: 1));
        Assert.Equal(HttpStatusCode.NotFound, otherBusinessOutlet.StatusCode);

        var unassignedWarehouse = await client.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            BuildPurchaseAndReceive("denied-warehouse", "warehouse", WarehouseId));
        Assert.Equal(HttpStatusCode.Forbidden, unassignedWarehouse.StatusCode);

        var invalidType = await client.PostAsJsonAsync(
            "/api/purchase-orders",
            BuildCreate("store", OutletId, quantity: 1));
        Assert.Equal(HttpStatusCode.BadRequest, invalidType.StatusCode);

        var assignedOutlet = await client.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            BuildPurchaseAndReceive("allowed-outlet", "outlet", OutletId));
        Assert.Equal(HttpStatusCode.OK, assignedOutlet.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var order = await db.PurchaseOrders.SingleAsync();
        Assert.Equal(OutletId, order.OutletId);
        Assert.Equal("outlet", (await db.Inventories.SingleAsync()).LocationType);
    }

    [Theory]
    [InlineData("outlet", OutletId)]
    [InlineData("warehouse", WarehouseId)]
    public async Task WildcardOwner_WithoutAssignments_CanPurchaseAndReceiveOnlyWithinBusiness(
        string locationType, long locationId)
    {
        using var app = CreateApp();
        await SeedAsync(app.Services, withOutlet: true, withWarehouse: true,
            accessScope: User.InventoryAccessAssignedOnly,
            step5Outlet: false,
            step5Warehouse: false);
        using var client = CreateClient(app);
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", "BusinessOwner");
        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "*");

        using var response = await client.PostAsJsonAsync("/api/purchase-orders/purchase-receive",
            BuildPurchaseAndReceive($"owner-{locationType}", locationType, locationId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var foreignResponse = await client.PostAsJsonAsync("/api/purchase-orders",
            BuildCreate("outlet", OtherBusinessOutletId, quantity: 1));
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventory = await db.Inventories.SingleAsync();
        Assert.Equal(locationType, inventory.LocationType);
        Assert.Equal(locationId, inventory.LocationId);
        Assert.Equal(3, inventory.Quantity);
    }

    private WebApplicationFactory<Program> CreateApp()
    {
        var journalPoster = new Mock<IPurchaseReceiptJournalPoster>();
        var completionPoster = new Mock<IGrnCompletionAccountingPoster>();
        completionPoster
            .Setup(poster => poster.PostReceiptAndCreatePayableAsync(
                It.IsAny<Grn>(),
                It.IsAny<PurchaseOrder>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPurchaseReceiptJournalPoster>();
                services.AddScoped<IPurchaseReceiptJournalPoster>(_ => journalPoster.Object);
                services.RemoveAll<IGrnCompletionAccountingPoster>();
                services.AddScoped<IGrnCompletionAccountingPoster>(_ => completionPoster.Object);
            }));
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", BusinessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", UserId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "PurchaseManager");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", AllPermissions);
        return client;
    }

    private static CreatePurchaseOrderDto BuildCreate(string? locationType, long? locationId, int quantity) =>
        new()
        {
            SupplierId = SupplierId,
            LocationType = locationType,
            LocationId = locationId,
            OrderDate = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            Status = "draft",
            Items =
            [
                new CreatePurchaseOrderItemDto
                {
                    VariantId = VariantId,
                    Quantity = quantity,
                    UnitPrice = 10m,
                    Unit = "pcs"
                }
            ]
        };

    private static CreatePurchaseAndReceiveDto BuildPurchaseAndReceive(
        string idempotencyKey,
        string? locationType,
        long? locationId,
        bool includeItems = true) =>
        new()
        {
            SupplierId = SupplierId,
            LocationType = locationType,
            LocationId = locationId,
            OrderDate = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            IdempotencyKey = idempotencyKey,
            Items = includeItems
                ? [new CreatePurchaseOrderItemDto { VariantId = VariantId, Quantity = 3, UnitPrice = 10m, Unit = "pcs" }]
                : []
        };

    private static async Task SeedAsync(
        IServiceProvider services,
        bool withOutlet,
        bool withWarehouse,
        string accessScope = User.InventoryAccessAll,
        long? assignedOutletId = null,
        bool step5Outlet = true,
        bool step5Warehouse = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(
            new Business { Id = BusinessId, Name = "Location Business", IsActive = true },
            new Business { Id = OtherBusinessId, Name = "Other Business", IsActive = true });
        db.Outlets.Add(new Outlet { Id = OtherBusinessOutletId, BusinessId = OtherBusinessId, Name = "Other Outlet" });
        if (withOutlet)
            db.Outlets.Add(new Outlet { Id = OutletId, BusinessId = BusinessId, Name = "Main Outlet" });
        if (withWarehouse)
            db.Warehouses.Add(new Warehouse { Id = WarehouseId, BusinessId = BusinessId, Name = "Central Warehouse" });

        db.Categories.Add(new Category { Id = 1501, BusinessId = BusinessId, Name = "Location Category" });
        db.Products.Add(new Product
        {
            Id = 1251,
            BusinessId = BusinessId,
            Name = "Location Product",
            ProductCode = "LOC-001",
            CategoryId = 1501,
            CostPrice = 10m,
            Status = ProductStatus.Active,
            HasVariants = true
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = VariantId,
            BusinessId = BusinessId,
            ProductId = 1251,
            Name = "Location Variant",
            Sku = "LOC-001-STD"
        });
        db.Suppliers.Add(new Supplier { Id = SupplierId, BusinessId = BusinessId, Name = "Location Supplier" });
        db.Users.Add(new User
        {
            Id = UserId,
            BusinessId = BusinessId,
            OutletId = assignedOutletId,
            Name = "Purchase Manager",
            Email = "location.purchaser@example.test",
            PasswordHash = "test",
            InventoryLocationAccessScope = accessScope
        });
        if (withOutlet && step5Outlet)
            db.UserOutletAssignments.Add(new UserOutletAssignment { Id = 1, UserId = UserId, OutletId = OutletId, BusinessId = BusinessId });
        if (withWarehouse && step5Warehouse)
            db.UserWarehouseAssignments.Add(new UserWarehouseAssignment { Id = 1, UserId = UserId, WarehouseId = WarehouseId, BusinessId = BusinessId });
        await db.SaveChangesAsync();
    }

    private static async Task<T?> ReadApiData<T>(HttpResponseMessage response) where T : class
    {
        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected success but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
        return envelope?.Data;
    }

    private sealed class ApiEnvelope<T> where T : class
    {
        public T? Data { get; set; }
    }
}
