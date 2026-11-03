using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.StockCounts;

namespace RetailPOS.Tests.Inventory;

public sealed class StockCountCreationSqliteAtomicityTests
{
    [Fact]
    public async Task Create_WhenLineInsertFails_RollsBackHeaderAndLines()
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
            setup.Businesses.Add(new Business { Id = 1, Name = "Stock Count Business" });
            setup.Categories.Add(new Category { Id = 201, BusinessId = 1, Name = "Stock Count Category" });
            setup.Outlets.Add(new Outlet
            {
                Id = 11,
                BusinessId = 1,
                Name = "Stock Count Outlet",
                Address = "Test"
            });
            setup.Products.Add(new Product
            {
                Id = 300,
                BusinessId = 1,
                Name = "Stock Count Product",
                ProductCode = "SC-001",
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
                Sku = "SC-001-DEF",
                Barcode = "SC-BC-001",
                Attributes = "{}"
            });
            setup.Users.Add(new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Stock Count User",
                Email = "stock-count@test.local",
                PasswordHash = "x",
                IsActive = true
            });
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_stock_count_line_insert " +
                "BEFORE INSERT ON stock_count_lines " +
                "BEGIN SELECT RAISE(ABORT, 'forced stock count rollback'); END;");
        }

        await using (var write = new RetailPOSDbContext(options))
        {
            var store = new StockCountCreationStore(write);
            await Assert.ThrowsAsync<DbUpdateException>(() => store.CreateAsync(
                new StockCountCreateInput(
                    1,
                    11,
                    "outlet",
                    new DateTime(2026, 10, 5),
                    null,
                    101),
                CancellationToken.None));
        }

        await using var verify = new RetailPOSDbContext(options);
        Assert.Equal(0, await verify.StockCounts.CountAsync());
        Assert.Equal(0, await verify.StockCountLines.CountAsync());
    }
}
