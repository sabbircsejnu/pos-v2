using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.StockCounts;

namespace RetailPOS.Tests.Inventory;

public sealed class StockCountUploadSqliteAtomicityTests
{
    [Fact]
    public async Task Upload_WhenLineUpdateFails_RollsBackPhysicalCountDifferenceAndRemarks()
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
            setup.Businesses.Add(new Business { Id = 1, Name = "Stock Count Upload Business" });
            setup.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Stock Count Upload Category" });
            setup.Outlets.Add(new Outlet
            {
                Id = 11,
                BusinessId = 1,
                Name = "Stock Count Upload Outlet",
                Address = "Test"
            });
            setup.Products.Add(new Product
            {
                Id = 300,
                BusinessId = 1,
                Name = "Stock Count Upload Product",
                ProductCode = "SC-UP-001",
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
                Sku = "SC-UP-001-DEF",
                Barcode = "SC-UP-BC-001",
                Attributes = "{}"
            });
            setup.Users.Add(new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Stock Count Upload User",
                Email = "stock-count-upload@test.local",
                PasswordHash = "x",
                IsActive = true
            });
            setup.StockCounts.Add(new StockCount
            {
                Id = 501,
                StockCountNo = "SC-20261005-0001",
                BusinessId = 1,
                LocationId = 11,
                LocationType = "outlet",
                StockCountDate = new DateTime(2026, 10, 5),
                Status = StockCount.StatusDraft,
                TotalItems = 1,
                CreatedBy = 101,
                CreatedAt = DateTime.UtcNow,
                Lines =
                [
                    new StockCountLine
                    {
                        Id = 601,
                        ProductId = 300,
                        VariantId = 301,
                        ProductName = "Stock Count Upload Product",
                        ProductCode = "SC-UP-001",
                        VariantName = "Default",
                        CurrentStock = 10m,
                        PhysicalStock = 9m,
                        Difference = -1m,
                        Remarks = "before"
                    }
                ]
            });
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_stock_count_line_update " +
                "BEFORE UPDATE ON stock_count_lines " +
                "BEGIN SELECT RAISE(ABORT, 'forced stock count upload rollback'); END;");
        }

        await using (var write = new RetailPOSDbContext(options))
        {
            var store = new StockCountUploadStore(
                write,
                NullLogger<StockCountUploadStore>.Instance);
            await Assert.ThrowsAsync<DbUpdateException>(() => store.UploadAsync(
                new StockCountUploadInput(
                    501,
                    1,
                    11,
                    "outlet",
                    101,
                    [
                        new StockCountUploadLineUpdate(
                            601,
                            12.5m,
                            2.5m,
                            "after")
                    ]),
                CancellationToken.None));
        }

        await using var verify = new RetailPOSDbContext(options);
        var line = await verify.StockCountLines.AsNoTracking().SingleAsync();
        Assert.Equal(9m, line.PhysicalStock);
        Assert.Equal(-1m, line.Difference);
        Assert.Equal("before", line.Remarks);
    }
}
