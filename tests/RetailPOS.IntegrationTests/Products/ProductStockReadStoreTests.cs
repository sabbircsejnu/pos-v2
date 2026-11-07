using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Application.Products.Queries;
using RetailPOS.Application.Products.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Products;

public sealed class ProductStockReadStoreTests
{
    [Fact]
    public async Task Sqlite_ListAndDetailMatchInventoryRecords()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var counter = new InventoryQueryCounter();
        await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection).AddInterceptors(counter).Options);
        await db.Database.EnsureCreatedAsync();
        await VerifyAsync(db, counter);
    }

    [Fact]
    [Trait("Category", "LiveInfrastructure")]
    public async Task Postgres_ListAndDetailMatchInventoryRecords()
    {
        var connectionString = InfrastructureTestSettings.Require(InfrastructureTestSettings.PostgresKey);
        InfrastructureTestSettings.AssertNotDevelopmentPostgres(connectionString);
        await PostgresTestDatabase.RunAsync(
            connectionString, PostgresTestDatabasePrefix.InventoryLocationAuthorization, async database =>
            {
                var counter = new InventoryQueryCounter();
                await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                    .UseNpgsql(database.ConnectionString).AddInterceptors(counter).Options);
                await db.Database.MigrateAsync();
                await VerifyAsync(db, counter);
            });
    }

    private static async Task VerifyAsync(RetailPOSDbContext db, InventoryQueryCounter counter)
    {
        db.Businesses.AddRange(new Business { Id = 1, Name = "Stock Business" }, new Business { Id = 2, Name = "Other Business" });
        db.Outlets.AddRange(
            new Outlet { Id = 1, BusinessId = 1, Name = "Selected" },
            new Outlet { Id = 2, BusinessId = 1, Name = "Second" },
            new Outlet { Id = 3, BusinessId = 2, Name = "Other business" },
            new Outlet { Id = 4, BusinessId = 1, Name = "Unassigned" });
        db.Warehouses.Add(new Warehouse { Id = 1, BusinessId = 1, Name = "Warehouse" });
        db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "Stock Category" });
        for (var id = 1; id <= 6; id++)
        {
            db.Products.Add(new Product
            {
                Id = id, BusinessId = 1, Name = $"Stock {id}", CategoryId = 1, HasVariants = id != 1,
                Status = id == 6 ? ProductStatus.Inactive : ProductStatus.Active
            });
            for (var variant = 1; variant <= (id == 1 ? 1 : 2); variant++)
                db.ProductVariants.Add(new ProductVariant
                {
                    Id = id * 10 + variant, BusinessId = 1, ProductId = id, Name = $"Variant {variant}", Sku = $"STOCK-{id}-{variant}"
                });
        }
        // Selected outlet: single stocked, partial, full, explicit zero, uninitialized.
        await db.SaveChangesAsync();
        RetailPOS.Core.Entities.Inventory[] rows =
        [
            Row(11, 1, "outlet", 7),
            Row(21, 1, "outlet", 5), Row(22, 1, "outlet", 0),
            Row(31, 1, "outlet", 2), Row(32, 1, "outlet", 3),
            Row(41, 1, "outlet", 0), Row(42, 1, "outlet", 0),
            Row(21, 2, "outlet", 0), Row(22, 2, "outlet", 9),
            Row(21, 1, "warehouse", 1), Row(22, 1, "warehouse", 1),
            Row(41, 3, "outlet", 100), Row(42, 4, "outlet", 100)
        ];
        if (db.Database.IsSqlite())
        {
            foreach (var row in rows)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold, xmin) VALUES ({row.VariantId}, {row.LocationId}, {row.LocationType}, {row.Quantity}, 10, 1)");
        }
        else
        {
            db.Inventories.AddRange(rows);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var scope = new TestBusinessScope(1);
        var readStore = new ProductReadStore(db, new ProductRepository(db, scope), new ProductVariantRepository(db, scope), new InventoryRepository(db), scope);
        var stockStore = new ProductStockReadStore(db, new ScopeProvider());
        var search = new SearchProductsQueryHandler(readStore, stockStore);
        var detail = new GetProductByIdQueryHandler(readStore, stockStore);
        var criteria = new ProductSearchCriteria(null, 1, "active", null, null, null, 1, 10, "name", "asc") { OutletId = 1 };
        counter.Count = 0;
        var result = await search.Handle(new SearchProductsQuery(criteria), default);
        Assert.Equal(1, counter.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(new[] { "in_stock", "partially_in_stock", "in_stock", "out_of_stock", "not_initialized" },
            result.Products.Select(product => product.StockStatus));
        Assert.Equal(new[] { 7m, 5m, 5m, 0m, 0m }, result.Products.Select(product => product.TotalStock));

        foreach (var product in result.Products)
        {
            var record = await detail.Handle(new GetProductByIdQuery(product.Id, 1), default);
            Assert.NotNull(record);
            Assert.Equal(product.StockStatus, record.StockStatus);
            var actualQuantity = await db.Inventories.Where(row => row.Variant.ProductId == product.Id
                && row.LocationType == "outlet" && row.LocationId == 1).SumAsync(row => row.Quantity);
            Assert.Equal(actualQuantity, record.TotalStock);
        }

        var secondOutlet = await detail.Handle(new GetProductByIdQuery(2, 2), default);
        Assert.Equal("partially_in_stock", secondOutlet!.StockStatus);
        Assert.Equal(9, secondOutlet.TotalStock);
        var warehouse = await detail.Handle(new GetProductByIdQuery(2, null, 1), default);
        Assert.Equal("in_stock", warehouse!.StockStatus);
        Assert.Equal(2, warehouse.TotalStock);
        var allAuthorized = await detail.Handle(new GetProductByIdQuery(2), default);
        Assert.Equal("in_stock", allAuthorized!.StockStatus);
        Assert.Equal(16, allAuthorized.TotalStock);
        var excluded = await detail.Handle(new GetProductByIdQuery(4), default);
        Assert.Equal("out_of_stock", excluded!.StockStatus);
        Assert.Equal(0, excluded.TotalStock);

        counter.Count = 0;
        var page = await search.Handle(new SearchProductsQuery(criteria with { PageNumber = 2, PageSize = 2, SortOrder = "desc" }), default);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(new long[] { 3, 2 }, page.Products.Select(product => product.Id));
        Assert.Equal(1, counter.Count);
        var filtered = await search.Handle(new SearchProductsQuery(criteria with { HasVariants = false }), default);
        Assert.Single(filtered.Products);
        Assert.Equal("in_stock", filtered.Products[0].StockStatus);

        counter.Count = 0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => detail.Handle(new GetProductByIdQuery(2, 3), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => detail.Handle(new GetProductByIdQuery(2, 4), default));
        await Assert.ThrowsAsync<ArgumentException>(() => detail.Handle(new GetProductByIdQuery(2, 1, 1), default));
        Assert.Equal(0, counter.Count);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static RetailPOS.Core.Entities.Inventory Row(long variantId, long locationId, string locationType, int quantity) =>
        new() { VariantId = variantId, LocationId = locationId, LocationType = locationType, Quantity = quantity };

    private sealed class ScopeProvider : IInventoryReportReadScopeProvider
    {
        public Task<InventoryReportReadScope> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InventoryReportReadScope(1, [1, 2, 3], [1]));
    }

    private sealed class InventoryQueryCounter : DbCommandInterceptor
    {
        public int Count { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"inventories\"", StringComparison.OrdinalIgnoreCase)
                || command.CommandText.Contains("FROM inventories ", StringComparison.OrdinalIgnoreCase))
                Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
