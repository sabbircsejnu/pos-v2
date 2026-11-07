using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Purchase;

/// <summary>
/// The real location rules behind every way of receiving goods: POST /api/grns, POST /api/grns/{id}/complete and
/// POST /api/purchase-orders/purchase-receive. Only the GRN stores are replaced, because the InMemory provider cannot
/// translate them; the user rows, the access policy and the purchase-and-receive flow are real.
/// </summary>
public sealed class GrnLocationAuthorizationHttpTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    private const long AssignedOutlet = 201;
    private const long DefaultOutlet = 202;
    private const long AssignedWarehouse = 301;
    private const long OtherWarehouse = 302;
    private const long ForeignOutlet = 209;

    private const long Owner = 1;
    private const long Assigned = 2;
    private const long Unassigned = 3;
    private const long WarehouseOnly = 4;
    private const long PlatformAdmin = 5;

    private const string Manager = "PurchaseManager";
    private const string BusinessOwner = "BusinessOwner";

    private const string Permissions =
        "purchases.view,purchases.create,purchases.receive,grn.view,grn.create,grn.receive";

    private readonly Mock<IGrnCreationStore> _creation = new();
    private readonly Mock<IGrnCompletionStore> _completion = new();

    [Theory]
    // Business Owner: every location of the Business, with no assignments.
    [InlineData(BusinessOwner, Owner, "outlet", DefaultOutlet, true)]
    [InlineData(BusinessOwner, Owner, "warehouse", OtherWarehouse, true)]
    // Super Admin: every location of the selected Business.
    [InlineData(SystemRoleNames.SuperAdmin, PlatformAdmin, "outlet", DefaultOutlet, true)]
    [InlineData(SystemRoleNames.SuperAdmin, PlatformAdmin, "warehouse", OtherWarehouse, true)]
    // Assigned user: Step 5 only; their Default Outlet and "All Locations" inventory scope do not count.
    [InlineData(Manager, Assigned, "outlet", AssignedOutlet, true)]
    [InlineData(Manager, Assigned, "warehouse", AssignedWarehouse, true)]
    [InlineData(Manager, Assigned, "outlet", DefaultOutlet, false)]
    [InlineData(Manager, Assigned, "warehouse", OtherWarehouse, false)]
    // No Step 5 rows: nothing, despite a Default Outlet and an "All Locations" inventory scope.
    [InlineData(Manager, Unassigned, "outlet", DefaultOutlet, false)]
    [InlineData(Manager, Unassigned, "warehouse", AssignedWarehouse, false)]
    // Warehouse-only user with no Default Outlet.
    [InlineData(Manager, WarehouseOnly, "warehouse", AssignedWarehouse, true)]
    [InlineData(Manager, WarehouseOnly, "warehouse", OtherWarehouse, false)]
    [InlineData(Manager, WarehouseOnly, "outlet", AssignedOutlet, false)]
    public async Task EveryReceivingEndpoint_FollowsTheGrnLocationRules(
        string role,
        long userId,
        string locationType,
        long locationId,
        bool allowed)
    {
        using var app = await CreateAppAsync(locationType, locationId);
        using var client = CreateClient(app, role, userId);

        using var created = await client.PostAsJsonAsync("/api/grns", CreateGrnRequest());
        using var completed = await client.PostAsync("/api/grns/40/complete", null);
        using var received = await client.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            PurchaseAndReceiveRequest(locationType, locationId));

        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, completed.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, received.StatusCode);

        _creation.Verify(
            store => store.CreateAsync(It.IsAny<GrnCreationRecord>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()),
            allowed ? Times.Once : Times.Never);
        _completion.Verify(
            store => store.CompleteAsync(40, userId, 1, It.IsAny<CancellationToken>()),
            allowed ? Times.Once : Times.Never);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(allowed ? 1 : 0, await db.Inventories.CountAsync());
        Assert.Equal(allowed ? 1 : 0, await db.Grns.CountAsync());
    }

    [Fact]
    public async Task Refusal_DoesNotNameTheLocationOfAnotherUsersGrn()
    {
        using var app = await CreateAppAsync("outlet", DefaultOutlet);
        using var client = CreateClient(app, Manager, Unassigned);

        using var response = await client.PostAsync("/api/grns/40/complete", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(DefaultOutlet.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SuperAdmin_WithoutASelectedBusiness_IsRejectedEverywhere()
    {
        using var app = await CreateAppAsync("outlet", DefaultOutlet);
        using var client = CreateClient(app, SystemRoleNames.SuperAdmin, PlatformAdmin, businessId: "none");

        using var created = await client.PostAsJsonAsync("/api/grns", CreateGrnRequest());
        using var completed = await client.PostAsync("/api/grns/40/complete", null);
        using var received = await client.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            PurchaseAndReceiveRequest("outlet", DefaultOutlet));

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, completed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, received.StatusCode);
        _creation.Verify(
            store => store.CreateAsync(It.IsAny<GrnCreationRecord>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _completion.Verify(
            store => store.CompleteAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BusinessOwner_CannotReceiveIntoAnotherBusinessLocation()
    {
        using var app = await CreateAppAsync("outlet", ForeignOutlet);
        _completion.Setup(store => store.FindScopeAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnCompletionScopeReadModel(2, ForeignOutlet, "outlet", "partial"));
        using var client = CreateClient(app, BusinessOwner, Owner);

        using var completed = await client.PostAsync("/api/grns/41/complete", null);
        using var received = await client.PostAsJsonAsync(
            "/api/purchase-orders/purchase-receive",
            PurchaseAndReceiveRequest("outlet", ForeignOutlet));

        Assert.Equal(HttpStatusCode.Forbidden, completed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, received.StatusCode);
    }

    [Fact]
    public async Task PurchaseOrderCreation_KeepsItsOwnLocationPolicy()
    {
        using var app = await CreateAppAsync("outlet", DefaultOutlet);
        using var client = CreateClient(app, Manager, Unassigned);

        // Step 3 still governs purchase orders; only receiving moved to Step 5.
        using var response = await client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId = 10,
            locationType = "outlet",
            locationId = DefaultOutlet,
            orderDate = "2026-10-04T00:00:00Z",
            status = "draft",
            items = new[] { new { variantId = 501, quantity = 1, unitPrice = 10m, unit = "pcs" } }
        });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static object CreateGrnRequest() => new
    {
        poId = 20,
        receivedDate = "2026-03-10T00:00:00Z",
        items = new[] { new { poItemId = 21, receivedQty = 1 } }
    };

    private static object PurchaseAndReceiveRequest(string locationType, long locationId) => new
    {
        supplierId = 10,
        locationType,
        locationId,
        orderDate = "2026-10-04T00:00:00Z",
        idempotencyKey = $"receive-{locationType}-{locationId}",
        items = new[] { new { variantId = 501, quantity = 1, unitPrice = 10m, unit = "pcs" } }
    };

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> app,
        string role,
        long userId,
        string businessId = "1")
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId);
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", Permissions);
        return client;
    }

    private async Task<WebApplicationFactory<Program>> CreateAppAsync(string locationType, long locationId)
    {
        var receipt = new GrnReadModel(
            40, 20, 10, "Supplier", null, string.Empty, new DateTime(2026, 3, 10), "partial",
            null, null, null, null, null, new DateTime(2026, 3, 10), []);
        _creation.Setup(store => store.FindPurchaseOrderAsync(20, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnCreationPurchaseOrderReadModel(
                20, 1, 10, locationId, locationType, "approved",
                [new GrnCreationPurchaseOrderItemReadModel(21, 5, 10m, 0m, 0m, null)]));
        _creation.Setup(store => store.GetCompletedReceivedQuantitiesAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<long, decimal>());
        _creation.Setup(store => store.CreateAsync(
                It.IsAny<GrnCreationRecord>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipt);
        _completion.Setup(store => store.FindScopeAsync(40, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrnCompletionScopeReadModel(1, locationId, locationType, "partial"));
        _completion.Setup(store => store.CompleteAsync(
                40,
                It.IsAny<long>(),
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipt);

        var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGrnCreationStore>();
                services.AddScoped<IGrnCreationStore>(_ => _creation.Object);
                services.RemoveAll<IGrnCompletionStore>();
                services.AddScoped<IGrnCompletionStore>(_ => _completion.Object);
                services.RemoveAll<IGrnCreationAuditWriter>();
                services.AddScoped<IGrnCreationAuditWriter>(_ => Mock.Of<IGrnCreationAuditWriter>());
                services.RemoveAll<IPurchaseReceiptJournalPoster>();
                services.AddScoped<IPurchaseReceiptJournalPoster>(_ => Mock.Of<IPurchaseReceiptJournalPoster>());
            }));
        await SeedAsync(app.Services);
        return app;
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Business 1", IsActive = true },
            new Business { Id = 2, Name = "Business 2", IsActive = true });
        db.Outlets.AddRange(
            new Outlet { Id = AssignedOutlet, BusinessId = 1, Name = "Assigned outlet" },
            new Outlet { Id = DefaultOutlet, BusinessId = 1, Name = "Default outlet" },
            new Outlet { Id = ForeignOutlet, BusinessId = 2, Name = "Foreign outlet" });
        db.Warehouses.AddRange(
            new Warehouse { Id = AssignedWarehouse, BusinessId = 1, Name = "Assigned warehouse" },
            new Warehouse { Id = OtherWarehouse, BusinessId = 1, Name = "Other warehouse" });
        db.Categories.Add(new Category { Id = 601, BusinessId = 1, Name = "Category" });
        db.Products.Add(new Product
        {
            Id = 401,
            BusinessId = 1,
            Name = "Product",
            ProductCode = "P-1",
            CategoryId = 601,
            CostPrice = 10m,
            Status = ProductStatus.Active,
            HasVariants = true
        });
        db.ProductVariants.Add(new ProductVariant { Id = 501, BusinessId = 1, ProductId = 401, Name = "Variant", Sku = "SKU-1" });
        db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "Supplier" });
        db.Users.AddRange(
            AddUser(Owner, 1, null, User.InventoryAccessAssignedOnly),
            AddUser(Assigned, 1, DefaultOutlet, User.InventoryAccessAll),
            AddUser(Unassigned, 1, DefaultOutlet, User.InventoryAccessAll),
            AddUser(WarehouseOnly, 1, null, User.InventoryAccessAssignedOnly),
            AddUser(PlatformAdmin, null, null, User.InventoryAccessAll));
        db.UserOutletAssignments.Add(new UserOutletAssignment { Id = 1, UserId = Assigned, OutletId = AssignedOutlet, BusinessId = 1 });
        db.UserWarehouseAssignments.AddRange(
            new UserWarehouseAssignment { Id = 1, UserId = Assigned, WarehouseId = AssignedWarehouse, BusinessId = 1 },
            new UserWarehouseAssignment { Id = 2, UserId = WarehouseOnly, WarehouseId = AssignedWarehouse, BusinessId = 1 });
        await db.SaveChangesAsync();
    }

    private static User AddUser(long id, long? businessId, long? outletId, string inventoryScope) => new()
    {
        Id = id,
        BusinessId = businessId,
        OutletId = outletId,
        Name = $"User {id}",
        Email = $"user{id}@example.test",
        PasswordHash = "test",
        InventoryLocationAccessScope = inventoryScope
    };
}
