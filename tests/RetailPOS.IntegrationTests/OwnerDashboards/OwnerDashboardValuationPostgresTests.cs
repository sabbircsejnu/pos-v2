using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RetailPOS.Application.OwnerDashboards;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.OwnerDashboards;
using RetailPOS.Tests.Infrastructure;
using RetailPOS.Tests.Inventory;

namespace RetailPOS.Tests.OwnerDashboards;

/// <summary>
/// Valued, proven-free and unvalued stock read from real PostgreSQL through the real read store and handlers. Master
/// cost and selling price are set high on purpose: if either leaked into a figure, these totals would not hold.
/// </summary>
public sealed class OwnerDashboardValuationPostgresTests
{
    private const long Business = 1;
    private const long Warehouse = 21;
    private const long PricedOutlet = 11;
    private const long UnvaluedOutlet = 12;
    private const long FreeOutlet = 13;
    private const long ForeignOutlet = 14;

    private const long Priced = 301;
    private const long NoLedger = 302;
    private const long UnprovenZero = 303;
    private const long ProvenFree = 304;
    private const long Mixed = 305;
    private const long ForeignVariant = 306;

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    [Fact]
    public async Task Summary_SeparatesValuedFreeAndUnvaluedStock() =>
        await RunAsync(async context =>
        {
            var handler = new GetInventorySummaryQueryHandler(Provider(), Store(context), new OwnerDashboardPolicy());

            var result = await handler.Handle(new GetInventorySummaryQuery(Filter()), CancellationToken.None);

            Assert.Equal(50m + 90m, result.TotalStockValue.Value);
            Assert.Equal(MetricStatuses.Partial, result.TotalStockValue.Status);
            var valuation = result.Valuation;
            Assert.Equal((2, 19L), (valuation.ValuedRecords, valuation.ValuedQuantity));
            Assert.Equal((1, 3L), (valuation.ZeroCostRecords, valuation.ZeroCostQuantity));
            Assert.Equal((3, 17L), (valuation.UnvaluedRecords, valuation.UnvaluedQuantity));
            Assert.Equal(39L, result.TotalStockQuantity);
            Assert.Equal(3, result.UnvaluedStockRows);
            Assert.Contains(result.DataQualityWarnings, warning => warning.Contains("17 unit(s)"));
        });

    [Fact]
    public async Task Locations_ReconcileToTheValuedSubtotal_AndNeverShowUnknownAsZero() =>
        await RunAsync(async context =>
        {
            var summary = await new GetInventorySummaryQueryHandler(Provider(), Store(context), new OwnerDashboardPolicy())
                .Handle(new GetInventorySummaryQuery(Filter()), CancellationToken.None);
            var result = await new GetStockValueByLocationQueryHandler(Provider(), Store(context))
                .Handle(new GetStockValueByLocationQuery(Filter()), CancellationToken.None);

            Assert.Equal(summary.TotalStockValue.Value, result.TotalStockValue);
            Assert.Equal(result.TotalStockValue, result.Locations.Sum(row => row.StockValue ?? 0m));
            Assert.Equal(
                summary.TotalStockQuantity,
                result.Locations.Sum(row => row.Valuation.ValuedQuantity + row.Valuation.ZeroCostQuantity + row.Valuation.UnvaluedQuantity));

            var warehouse = result.Locations.Single(row => row.LocationId == Warehouse);
            Assert.Equal(StockValuationStatuses.Partial, warehouse.Valuation.Status);
            Assert.Equal(50m, warehouse.StockValue);
            Assert.Equal((10L, 7L), (warehouse.Valuation.ValuedQuantity, warehouse.Valuation.UnvaluedQuantity));

            var unvalued = result.Locations.Single(row => row.LocationId == UnvaluedOutlet);
            Assert.Equal(StockValuationStatuses.NotValued, unvalued.Valuation.Status);
            Assert.Null(unvalued.StockValue);
            Assert.Null(unvalued.PercentOfTotal);
            Assert.Equal(10L, unvalued.Quantity);

            var free = result.Locations.Single(row => row.LocationId == FreeOutlet);
            Assert.Equal(StockValuationStatuses.ZeroCost, free.Valuation.Status);
            Assert.Equal(0m, free.StockValue);

            // An unknown value sorts after every known one.
            Assert.Equal(UnvaluedOutlet, result.Locations.Last(row => row.Quantity > 0).LocationId);
        });

    [Fact]
    public async Task OneUnprovenZero_SpoilsThatBalance_EvenWhenItsTotalIsPositive() =>
        await RunAsync(async context =>
        {
            var snapshot = await Store(context).GetSnapshotAsync(Scope(), From, To, true, CancellationToken.None);

            var mixed = snapshot.Facts.Single(fact => fact.VariantId == Mixed);
            Assert.Equal(6, mixed.OnHand);
            Assert.Null(mixed.StockValue);
            Assert.Null(snapshot.Facts.Single(fact => fact.VariantId == UnprovenZero).StockValue);
            Assert.Null(snapshot.Facts.Single(fact => fact.VariantId == NoLedger).StockValue);
            Assert.Equal(0m, snapshot.Facts.Single(fact => fact.VariantId == ProvenFree).StockValue);
            Assert.Equal(50m, snapshot.Facts.Single(fact => fact.VariantId == Priced && fact.LocationId == Warehouse).StockValue);
        });

    [Fact]
    public async Task ProductInsights_UseTheSameRules() =>
        await RunAsync(async context =>
        {
            var handler = new GetProductInsightsQueryHandler(Provider(), Store(context));

            var noLedger = await handler.Handle(
                new GetProductInsightsQuery(402, null, Filter()),
                CancellationToken.None);
            Assert.Null(noLedger.TotalStockValue);
            Assert.Equal(StockValuationStatuses.NotValued, noLedger.Valuation.Status);

            var free = await handler.Handle(
                new GetProductInsightsQuery(404, null, Filter()),
                CancellationToken.None);
            Assert.Equal(0m, free.TotalStockValue);
            Assert.Equal(StockValuationStatuses.ZeroCost, free.Valuation.Status);
        });

    [Fact]
    public async Task AnotherBusinessesStock_NeverEntersAFigure() =>
        await RunAsync(async context =>
        {
            var snapshot = await Store(context).GetSnapshotAsync(Scope(), From, To, true, CancellationToken.None);

            Assert.DoesNotContain(snapshot.Facts, fact => fact.VariantId == ForeignVariant || fact.LocationId == ForeignOutlet);
        });

    private static InventoryDashboardReadStore Store(RetailPOSDbContext context) => new(context, TimeProvider.System);

    private static InventoryFilter Filter() => new(From, To, null, null, null, null);

    private static IOwnerDashboardScopeProvider Provider()
    {
        var provider = new Mock<IOwnerDashboardScopeProvider>();
        provider.Setup(item => item.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Scope());
        return provider.Object;
    }

    private static OwnerDashboardScope Scope() => new(
        Business,
        "Asia/Dhaka",
        To,
        [
            new OwnerDashboardLocation(PricedOutlet, "Priced", OwnerDashboardLocationTypes.Outlet),
            new OwnerDashboardLocation(UnvaluedOutlet, "Unvalued", OwnerDashboardLocationTypes.Outlet),
            new OwnerDashboardLocation(FreeOutlet, "Free", OwnerDashboardLocationTypes.Outlet)
        ],
        [new OwnerDashboardLocation(Warehouse, "Central", OwnerDashboardLocationTypes.Warehouse)],
        [],
        null,
        false,
        false);

    private static Task RunAsync(Func<RetailPOSDbContext, Task> scenario) =>
        PostgresTestDatabase.RunAsync(
            Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
                ?? throw new InvalidOperationException("RETAILPOS_TEST_POSTGRES must target the Test stack."),
            PostgresTestDatabasePrefix.OwnerDashboard,
            async database =>
            {
                await using var context = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                    .UseNpgsql(database.ConnectionString)
                    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                    .Options);
                await context.Database.MigrateAsync();
                await SeedAsync(context);
                context.ChangeTracker.Clear();
                await scenario(context);
            });

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(
            new Business { Id = Business, Name = "Valuation", IsActive = true },
            new Business { Id = 2, Name = "Foreign", IsActive = true });
        db.Categories.AddRange(
            new Category { Id = 201, BusinessId = Business, Name = "Valuation" },
            new Category { Id = 202, BusinessId = 2, Name = "Foreign" });
        db.Outlets.AddRange(
            new Outlet { Id = PricedOutlet, BusinessId = Business, Name = "Priced", Address = "A" },
            new Outlet { Id = UnvaluedOutlet, BusinessId = Business, Name = "Unvalued", Address = "B" },
            new Outlet { Id = FreeOutlet, BusinessId = Business, Name = "Free", Address = "C" },
            new Outlet { Id = ForeignOutlet, BusinessId = 2, Name = "Foreign", Address = "D" });
        db.Warehouses.Add(new Warehouse { Id = Warehouse, BusinessId = Business, Name = "Central", Address = "E" });

        foreach (var (variant, product) in new[]
                 {
                     (Priced, 401L), (NoLedger, 402L), (UnprovenZero, 403L), (ProvenFree, 404L), (Mixed, 405L)
                 })
        {
            db.Products.Add(new Product
            {
                Id = product,
                BusinessId = Business,
                Name = $"Product {product}",
                ProductCode = $"VAL-{product}",
                CategoryId = 201,
                CostPrice = 4000m,
                BasePrice = 5000m,
                Status = ProductStatus.Active
            });
            db.ProductVariants.Add(new ProductVariant
            {
                Id = variant,
                BusinessId = Business,
                ProductId = product,
                Name = $"Variant {variant}",
                Sku = $"VAL-{variant}",
                Attributes = "{}"
            });
        }

        db.Products.Add(new Product { Id = 406, BusinessId = 2, Name = "Foreign", ProductCode = "VAL-F", CategoryId = 202, Status = ProductStatus.Active });
        db.ProductVariants.Add(new ProductVariant { Id = ForeignVariant, BusinessId = 2, ProductId = 406, Name = "Foreign", Sku = "VAL-F", Attributes = "{}" });

        db.Inventories.AddRange(
            Stock(Priced, Warehouse, "warehouse", 10),
            Stock(NoLedger, Warehouse, "warehouse", 7),
            Stock(Priced, PricedOutlet, "outlet", 9),
            Stock(UnprovenZero, UnvaluedOutlet, "outlet", 4),
            Stock(Mixed, UnvaluedOutlet, "outlet", 6),
            Stock(ProvenFree, FreeOutlet, "outlet", 3),
            Stock(ForeignVariant, ForeignOutlet, "outlet", 99));
        db.StockLedgers.AddRange(
            StockCostTestData.Receipt(Priced, Warehouse, "warehouse", 10, 5m),
            StockCostTestData.Receipt(Priced, PricedOutlet, "outlet", 9, 10m),
            StockCostTestData.Receipt(UnprovenZero, UnvaluedOutlet, "outlet", 4, 0m, referenceId: 800),
            StockCostTestData.Receipt(ProvenFree, FreeOutlet, "outlet", 3, 0m, referenceId: 801),
            StockCostTestData.Receipt(Mixed, UnvaluedOutlet, "outlet", 3, 0m, sequence: 1, referenceId: 802),
            StockCostTestData.Receipt(Mixed, UnvaluedOutlet, "outlet", 3, 8m, sequence: 2, referenceId: 803),
            StockCostTestData.Receipt(ForeignVariant, ForeignOutlet, "outlet", 99, 1000m));

        // A document proves one zero. GRN 800 exists too, but its order priced the goods, so its zero stays unproven.
        db.Suppliers.Add(new Supplier { Id = 701, BusinessId = Business, Name = "Supplier" });
        db.PurchaseOrders.AddRange(
            new PurchaseOrder
            {
                Id = 702,
                PoNumber = "PO-VAL-FREE",
                SupplierId = 701,
                OutletId = FreeOutlet,
                OrderDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = "completed",
                Items = [new PurchaseOrderItem { Id = 703, VariantId = ProvenFree, Quantity = 3, UnitPrice = 0m }]
            },
            new PurchaseOrder
            {
                Id = 705,
                PoNumber = "PO-VAL-PRICED",
                SupplierId = 701,
                OutletId = UnvaluedOutlet,
                OrderDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = "completed",
                Items = [new PurchaseOrderItem { Id = 706, VariantId = UnprovenZero, Quantity = 4, UnitPrice = 25m }]
            });
        db.Grns.AddRange(
            new Grn
            {
                Id = 801,
                PoId = 702,
                ReceivedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = "completed",
                Items = [new GrnItem { Id = 704, PoItemId = 703, ReceivedQty = 3, UnitCost = 0m }]
            },
            new Grn
            {
                Id = 800,
                PoId = 705,
                ReceivedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = "completed",
                Items = [new GrnItem { Id = 707, PoItemId = 706, ReceivedQty = 4, UnitCost = 0m }]
            });
        await db.SaveChangesAsync();
    }

    private static RetailPOS.Core.Entities.Inventory Stock(long variantId, long locationId, string type, int quantity) => new()
    {
        VariantId = variantId,
        LocationId = locationId,
        LocationType = type,
        Quantity = quantity
    };
}
