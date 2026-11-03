using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.StockCounts;

namespace RetailPOS.Tests.Inventory;

public sealed class StockCountAdjustmentDraftSqliteAtomicityTests
{
    [Fact]
    public async Task Generate_WhenAdjustmentInsertFails_RollsBackSourceStatusAndDraft()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using (var setup = new RetailPOSDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Businesses.Add(new Business { Id = 1, Name = "Stock Count Adjustment Business" });
            setup.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Stock Count Adjustment Category" });
            setup.Outlets.Add(new Outlet
            {
                Id = 11,
                BusinessId = 1,
                Name = "Stock Count Adjustment Outlet",
                Address = "Test"
            });
            setup.Products.Add(new Product
            {
                Id = 300,
                BusinessId = 1,
                Name = "Stock Count Adjustment Product",
                ProductCode = "SCA-001",
                CategoryId = 201,
                BasePrice = 100m,
                CostPrice = 70m,
                HasVariants = true,
                Status = ProductStatus.Active
            });
            setup.ProductVariants.Add(new ProductVariant
            {
                Id = 301,
                BusinessId = 1,
                ProductId = 300,
                Name = "Default",
                Sku = "SCA-001-DEF",
                Barcode = "SCA-BC-001",
                Attributes = "{}"
            });
            setup.Users.Add(new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Stock Count Adjustment User",
                Email = "stock-count-adjustment@test.local",
                PasswordHash = "x",
                IsActive = true
            });
            setup.StockCounts.Add(new StockCount
            {
                Id = 501,
                StockCountNo = "SC-ADJ-ROLLBACK",
                BusinessId = 1,
                LocationId = 11,
                LocationType = "outlet",
                StockCountDate = new DateTime(2026, 10, 5),
                Status = StockCount.StatusApproved,
                TotalItems = 1,
                CreatedBy = 101,
                CreatedAt = DateTime.UtcNow,
                ApprovedBy = 101,
                ApprovedAt = DateTime.UtcNow,
                Lines =
                [
                    new StockCountLine
                    {
                        ProductId = 300,
                        VariantId = 301,
                        ProductName = "Stock Count Adjustment Product",
                        ProductCode = "SCA-001",
                        VariantName = "Default",
                        CurrentStock = 10,
                        PhysicalStock = 13,
                        Difference = 3
                    }
                ]
            });
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync(
                "INSERT INTO inventories " +
                "(variant_id, location_id, location_type, quantity, low_stock_threshold, xmin) " +
                "VALUES (301, 11, 'outlet', 10, 10, 0);");
            await setup.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_stock_count_adjustment_insert " +
                "BEFORE INSERT ON stock_adjustments " +
                "BEGIN SELECT RAISE(ABORT, 'forced stock count adjustment rollback'); END;");
        }

        await using (var write = new RetailPOSDbContext(options))
        {
            var store = new StockCountAdjustmentDraftStore(
                write,
                NullLogger<StockCountAdjustmentDraftStore>.Instance);
            await Assert.ThrowsAsync<DbUpdateException>(() => store.GenerateAsync(
                501,
                1,
                11,
                "outlet",
                101,
                CancellationToken.None));
        }

        await using var verify = new RetailPOSDbContext(options);
        var stockCount = await verify.StockCounts.AsNoTracking().SingleAsync();
        Assert.Equal(StockCount.StatusApproved, stockCount.Status);
        Assert.Empty(await verify.StockAdjustments.ToListAsync());
        Assert.Empty(await verify.StockAdjustmentLines.ToListAsync());
    }
}
