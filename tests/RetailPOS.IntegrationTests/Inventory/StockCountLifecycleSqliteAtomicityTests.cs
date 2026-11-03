using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.StockCounts;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Inventory;

public sealed class StockCountLifecycleSqliteAtomicityTests
{
    [Fact]
    public async Task Reopen_WhenHeaderUpdateFails_RollsBackStatusAndRejectionEvidence()
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
            setup.Businesses.Add(new Business { Id = 1, Name = "Stock Count Lifecycle Business" });
            setup.Outlets.Add(new Outlet
            {
                Id = 11,
                BusinessId = 1,
                Name = "Stock Count Lifecycle Outlet",
                Address = "Test"
            });
            setup.Users.Add(new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Stock Count Lifecycle User",
                Email = "stock-count-lifecycle@test.local",
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
                Status = StockCount.StatusRejected,
                CreatedBy = 101,
                CreatedAt = DateTime.UtcNow,
                SubmittedBy = 101,
                SubmittedAt = DateTime.UtcNow.AddMinutes(-5),
                RejectedBy = 101,
                RejectedAt = DateTime.UtcNow,
                RejectionReason = "recount"
            });
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_stock_count_lifecycle_update " +
                "BEFORE UPDATE ON stock_counts " +
                "BEGIN SELECT RAISE(ABORT, 'forced stock count lifecycle rollback'); END;");
        }

        await using (var write = new RetailPOSDbContext(options))
        {
            var store = new StockCountLifecycleStore(write);
            await Assert.ThrowsAsync<DbUpdateException>(() => store.ReopenAsync(
                501,
                1,
                11,
                "outlet",
                101,
                CancellationToken.None));
        }

        await using var verify = new RetailPOSDbContext(options);
        var stockCount = await verify.StockCounts.AsNoTracking().SingleAsync();
        Assert.Equal(StockCount.StatusRejected, stockCount.Status);
        Assert.Equal(101, stockCount.RejectedBy);
        Assert.NotNull(stockCount.RejectedAt);
        Assert.Equal("recount", stockCount.RejectionReason);
        Assert.Equal(101, stockCount.SubmittedBy);
        Assert.NotNull(stockCount.SubmittedAt);
    }
}
