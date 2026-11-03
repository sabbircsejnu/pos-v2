using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.Services;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.Commands;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Grns;
using RetailPOS.Infrastructure.PurchaseOrders;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Purchase;

/// <summary>
/// PostgreSQL coverage for Outlet/Warehouse purchase destinations: migration constraints, SQL
/// translation of the destination-aware Business scope, and manual partial GRNs into an Outlet.
/// </summary>
public sealed class PurchaseLocationDestinationRelationalTests
{
    private const long BusinessId = 1;
    private const long OtherBusinessId = 2;
    private const long OutletId = 1201;
    private const long WarehouseId = 1101;
    private const long SupplierId = 1001;
    private const long VariantId = 5001;
    private const long UserId = 77;

    static PurchaseLocationDestinationRelationalTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task OutletOnlyBusiness_ApprovedPurchaseOrder_PartialGrnsReceiveIntoOutlet()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.PurchaseReceive,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db, withOutlet: true, withWarehouse: false);
                var purchaseScope = new FixedPurchaseOrderTenantScope(BusinessId);
                var locationAuthorizer = new Mock<IPurchaseOrderLocationAuthorizer>();

                var created = await new CreatePurchaseOrderCommandHandler(
                        new PurchaseOrderCreationStore(db),
                        new PurchaseOrderReadStore(db),
                        purchaseScope,
                        locationAuthorizer.Object)
                    .Handle(
                        new CreatePurchaseOrderCommand(
                            SupplierId,
                            new PurchaseOrderDestination("outlet", OutletId),
                            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
                            null,
                            "pending",
                            null,
                            [new CreatePurchaseOrderItemCommand(VariantId, 5, 10m, 0m, 0m, "pcs")],
                            UserId),
                        CancellationToken.None);
                Assert.Equal("outlet", created.LocationType);
                Assert.Equal("Main Outlet", created.LocationName);
                Assert.Null(created.WarehouseId);

                var approved = await new ApprovePurchaseOrderCommandHandler(
                        new PurchaseOrderUpdateStore(db),
                        new PurchaseOrderReadStore(db),
                        purchaseScope)
                    .Handle(new ApprovePurchaseOrderCommand(created.Id), CancellationToken.None);
                Assert.Equal("approved", approved.Status);
                Assert.Equal(OutletId, approved.LocationId);

                var poItemId = Assert.Single(approved.Items).Id;
                var completionAuthorizer = new Mock<IGrnCompletionAuthorizer>();
                completionAuthorizer
                    .Setup(authorizer => authorizer.AuthorizeAsync(
                        BusinessId,
                        OutletId,
                        "outlet",
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BusinessId);

                await ReceiveAsync(db, created.Id, poItemId, 2, locationAuthorizer.Object, completionAuthorizer.Object);

                db.ChangeTracker.Clear();
                var inventory = await db.Inventories.SingleAsync();
                Assert.Equal(("outlet", OutletId, 2), (inventory.LocationType, inventory.LocationId, inventory.Quantity));
                Assert.Equal("partial", (await db.PurchaseOrders.SingleAsync()).Status);

                await ReceiveAsync(db, created.Id, poItemId, 3, locationAuthorizer.Object, completionAuthorizer.Object);

                db.ChangeTracker.Clear();
                Assert.Equal(5, await db.Inventories.Select(item => item.Quantity).SingleAsync());
                Assert.Equal("received", (await db.PurchaseOrders.SingleAsync()).Status);
                var ledgers = await db.StockLedgers.ToListAsync();
                Assert.Equal(2, ledgers.Count);
                Assert.All(ledgers, ledger => Assert.Equal(("outlet", OutletId), (ledger.LocationType, ledger.LocationId)));
                Assert.Empty(await db.Warehouses.ToListAsync());
                locationAuthorizer.Verify(
                    authorizer => authorizer.AuthorizeAsync("outlet", OutletId, It.IsAny<CancellationToken>()),
                    Times.Exactly(3));
                completionAuthorizer.Verify(
                    authorizer => authorizer.AuthorizeAsync(BusinessId, OutletId, "outlet", It.IsAny<CancellationToken>()),
                    Times.Exactly(2));
            });
    }

    [Fact]
    public async Task MixedBusiness_DestinationsAreBusinessScopedAndIdempotentPerLocation()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.PurchaseReceive,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db, withOutlet: true, withWarehouse: true);
                var handler = CreatePurchaseAndReceiveHandler(db, BusinessId);

                var outlet = await handler.Handle(
                    BuildPurchaseAndReceive("same-key", new PurchaseOrderDestination("outlet", OutletId)),
                    CancellationToken.None);
                var warehouse = await handler.Handle(
                    BuildPurchaseAndReceive("same-key", new PurchaseOrderDestination("warehouse", WarehouseId)),
                    CancellationToken.None);
                var outletDuplicate = await handler.Handle(
                    BuildPurchaseAndReceive("same-key", new PurchaseOrderDestination("outlet", OutletId)),
                    CancellationToken.None);

                Assert.NotEqual(outlet.PurchaseOrder.Id, warehouse.PurchaseOrder.Id);
                Assert.True(outletDuplicate.IsDuplicateRequest);
                Assert.Equal(outlet.PurchaseOrder.Id, outletDuplicate.PurchaseOrder.Id);
                Assert.Equal("outlet", outlet.PurchaseOrder.LocationType);
                Assert.Equal("warehouse", warehouse.PurchaseOrder.LocationType);

                db.ChangeTracker.Clear();
                var stock = await db.Inventories
                    .OrderBy(item => item.LocationType)
                    .Select(item => new { item.LocationType, item.LocationId, item.Quantity })
                    .ToListAsync();
                Assert.Equal(2, stock.Count);
                Assert.Equal(new { LocationType = "outlet", LocationId = OutletId, Quantity = 3 }, stock[0]);
                Assert.Equal(new { LocationType = "warehouse", LocationId = WarehouseId, Quantity = 3 }, stock[1]);

                var readStore = new PurchaseOrderReadStore(db);
                Assert.Null(await readStore.FindByIdAsync(outlet.PurchaseOrder.Id, OtherBusinessId, CancellationToken.None));
                Assert.NotNull(await readStore.FindByIdAsync(outlet.PurchaseOrder.Id, BusinessId, CancellationToken.None));
                var outletOnly = await readStore.SearchAsync(
                    null, null, "outlet", null, null, null, 1, 10, "order_date", "desc", BusinessId, CancellationToken.None);
                Assert.Equal(outlet.PurchaseOrder.Id, Assert.Single(outletOnly.PurchaseOrders).Id);
                var grns = await new GrnReadStore(db).FindAllAsync(null, null, BusinessId, CancellationToken.None);
                Assert.Equal(2, grns.Count);
                Assert.Contains(grns, grn => grn.LocationType == "outlet" && grn.LocationName == "Main Outlet");
                Assert.Empty(await new GrnReadStore(db).FindAllAsync(null, null, OtherBusinessId, CancellationToken.None));

                var otherBusinessHandler = CreatePurchaseAndReceiveHandler(db, OtherBusinessId);
                var crossBusiness = await Assert.ThrowsAsync<KeyNotFoundException>(() => otherBusinessHandler.Handle(
                    BuildPurchaseAndReceive("cross-business", new PurchaseOrderDestination("outlet", OutletId)),
                    CancellationToken.None));
                Assert.Equal($"Outlet with ID {OutletId} not found", crossBusiness.Message);
            });
    }

    [Fact]
    public async Task MixedBusiness_DraftDestinationCanMoveFromWarehouseToOutlet()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.PurchaseReceive,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db, withOutlet: true, withWarehouse: true);
                var scope = new FixedPurchaseOrderTenantScope(BusinessId);
                var authorizer = new Mock<IPurchaseOrderLocationAuthorizer>();
                var created = await new CreatePurchaseOrderCommandHandler(
                        new PurchaseOrderCreationStore(db),
                        new PurchaseOrderReadStore(db),
                        scope,
                        authorizer.Object)
                    .Handle(
                        new CreatePurchaseOrderCommand(
                            SupplierId,
                            new PurchaseOrderDestination("warehouse", WarehouseId),
                            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
                            null,
                            "draft",
                            null,
                            [new CreatePurchaseOrderItemCommand(VariantId, 2, 10m, 0m, 0m, "pcs")],
                            UserId),
                        CancellationToken.None);
                Assert.Equal("warehouse", created.LocationType);
                db.ChangeTracker.Clear();

                var updated = await new UpdatePurchaseOrderCommandHandler(
                        new PurchaseOrderUpdateStore(db),
                        new PurchaseOrderReadStore(db),
                        scope,
                        new PurchaseOrderCreationStore(db),
                        authorizer.Object)
                    .Handle(
                        new UpdatePurchaseOrderCommand(
                            created.Id,
                            SupplierId,
                            new PurchaseOrderDestination("outlet", OutletId),
                            new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
                            null,
                            null,
                            [new UpdatePurchaseOrderItemCommand(VariantId, 3, 10m, 0m, 0m, "pcs")]),
                        CancellationToken.None);

                Assert.Equal("outlet", updated.LocationType);
                Assert.Equal("Main Outlet", updated.LocationName);
                db.ChangeTracker.Clear();
                var persisted = await db.PurchaseOrders.SingleAsync();
                Assert.Equal(OutletId, persisted.OutletId);
                Assert.Null(persisted.WarehouseId);
            });
    }

    [Fact]
    public async Task Migration_RequiresExactlyOneDestinationAndKeepsWarehouseRows()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.PurchaseReceive,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db, withOutlet: true, withWarehouse: true);

                db.PurchaseOrders.Add(new PurchaseOrder
                {
                    PoNumber = "PO-NO-DESTINATION",
                    SupplierId = SupplierId,
                    OrderDate = DateTime.UtcNow,
                    Status = "draft"
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                db.ChangeTracker.Clear();

                db.PurchaseOrders.Add(new PurchaseOrder
                {
                    PoNumber = "PO-BOTH-DESTINATIONS",
                    SupplierId = SupplierId,
                    WarehouseId = WarehouseId,
                    OutletId = OutletId,
                    OrderDate = DateTime.UtcNow,
                    Status = "draft"
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                db.ChangeTracker.Clear();

                db.PurchaseOrders.Add(new PurchaseOrder
                {
                    PoNumber = "PO-LEGACY-WAREHOUSE",
                    SupplierId = SupplierId,
                    WarehouseId = WarehouseId,
                    OrderDate = DateTime.UtcNow,
                    Status = "draft"
                });
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                var legacy = await new PurchaseOrderReadStore(db).FindAllAsync(
                    null, null, null, WarehouseId, BusinessId, CancellationToken.None);
                var order = Assert.Single(legacy);
                Assert.Equal("warehouse", order.LocationType);
                Assert.Equal("Central Warehouse", order.LocationName);
            });
    }

    private static async Task ReceiveAsync(
        RetailPOSDbContext db,
        long purchaseOrderId,
        long poItemId,
        int quantity,
        IPurchaseOrderLocationAuthorizer locationAuthorizer,
        IGrnCompletionAuthorizer completionAuthorizer)
    {
        var grnScope = new Mock<IGrnTenantScope>();
        grnScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(BusinessId);
        var grn = await new CreateGrnCommandHandler(
                new GrnCreationStore(db, NullLogger<GrnCreationStore>.Instance),
                grnScope.Object,
                Mock.Of<IGrnCreationAuditWriter>(),
                locationAuthorizer)
            .Handle(
                new CreateGrnCommand(
                    purchaseOrderId,
                    DateTime.UtcNow,
                    null,
                    null,
                    null,
                    [new CreateGrnItemCommand(poItemId, quantity, null, null)],
                    UserId),
                CancellationToken.None);
        Assert.Equal("outlet", grn.LocationType);

        var accounting = new Mock<IGrnCompletionAccountingPoster>();
        accounting
            .Setup(poster => poster.PostReceiptAndCreatePayableAsync(
                It.IsAny<Grn>(),
                It.IsAny<PurchaseOrder>(),
                BusinessId,
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);
        var ledger = new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
            db,
            NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance);
        var completed = await new CompleteGrnCommandHandler(
                new GrnCompletionStore(
                    db,
                    new GrnCompletionStockLedgerWriterAdapter(ledger),
                    accounting.Object,
                    Mock.Of<IGrnCompletionAuditWriter>(),
                    NullLogger<GrnCompletionStore>.Instance),
                completionAuthorizer)
            .Handle(new CompleteGrnCommand(grn.Id, UserId), CancellationToken.None);
        Assert.Equal("completed", completed.Status);
    }

    private static PurchaseAndReceiveCommandHandler CreatePurchaseAndReceiveHandler(
        RetailPOSDbContext db,
        long businessId)
    {
        var ledger = new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
            db,
            NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance);
        return new PurchaseAndReceiveCommandHandler(
            new PurchaseAndReceiveStore(
                db,
                new PurchaseAndReceiveStockLedgerWriterAdapter(ledger),
                Mock.Of<IPurchaseReceiptJournalPoster>(),
                Mock.Of<ILogger<PurchaseAndReceiveStore>>()),
            new PurchaseOrderReadStore(db),
            new FixedPurchaseOrderTenantScope(businessId),
            Mock.Of<IPurchaseOrderLocationAuthorizer>());
    }

    private static PurchaseAndReceiveCommand BuildPurchaseAndReceive(
        string key,
        PurchaseOrderDestination destination) =>
        new(
            SupplierId,
            destination,
            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            null,
            null,
            [new PurchaseAndReceiveItemCommand(VariantId, 3, 10m, 0m, 0m, "pcs")],
            key,
            UserId);

    private static async Task SeedAsync(RetailPOSDbContext db, bool withOutlet, bool withWarehouse)
    {
        db.Businesses.AddRange(
            new Business { Id = BusinessId, Name = "Business 1", IsActive = true },
            new Business { Id = OtherBusinessId, Name = "Business 2", IsActive = true });
        if (withOutlet)
            db.Outlets.Add(new Outlet { Id = OutletId, BusinessId = BusinessId, Name = "Main Outlet" });
        if (withWarehouse)
            db.Warehouses.Add(new Warehouse { Id = WarehouseId, BusinessId = BusinessId, Name = "Central Warehouse" });
        db.Categories.Add(new Category { Id = 3001, BusinessId = BusinessId, Name = "Category" });
        db.Suppliers.AddRange(
            new Supplier { Id = SupplierId, BusinessId = BusinessId, Name = "Supplier 1" },
            new Supplier { Id = 2001, BusinessId = OtherBusinessId, Name = "Supplier 2" });
        db.Users.Add(new User
        {
            Id = UserId,
            BusinessId = BusinessId,
            Name = "Purchaser",
            Email = "purchaser@example.test",
            PasswordHash = "test"
        });
        db.Products.Add(new Product { Id = 4001, BusinessId = BusinessId, Name = "Product", CategoryId = 3001, CostPrice = 10m });
        db.ProductVariants.Add(new ProductVariant { Id = VariantId, BusinessId = BusinessId, ProductId = 4001, Sku = "SKU-1", Name = "Variant" });
        await db.SaveChangesAsync();
    }

    private static async Task<RetailPOSDbContext> CreateMigratedContextAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return await PostgresTestDatabase.InitializeResourceAsync(
            new RetailPOSDbContext(options),
            context => context.Database.MigrateAsync());
    }

    private static string GetPostgresConnectionString()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configuredConnection))
            return configuredConnection;

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build()
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("RetailPOS PostgreSQL test connection is not configured.");
    }

    private sealed class FixedPurchaseOrderTenantScope(long businessId) : IPurchaseOrderTenantScope
    {
        public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult<long?>(businessId);
    }
}
