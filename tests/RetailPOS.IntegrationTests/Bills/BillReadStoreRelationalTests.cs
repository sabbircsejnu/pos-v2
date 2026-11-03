using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Bills;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.IntegrationTests.Bills;

public sealed class BillReadStoreRelationalTests
{
    [Fact]
    public async Task ReadStore_PreservesBusinessScopedPagingDetailAndSummary()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var localBusiness = new Business { Id = 17, Name = "Business A" };
        var foreignBusiness = new Business { Id = 18, Name = "Business B" };
        var localSupplier = new Supplier { Id = 10, BusinessId = 17, Name = "Local Supplier" };
        var foreignSupplier = new Supplier { Id = 20, BusinessId = 18, Name = "Foreign Supplier" };
        db.Businesses.AddRange(localBusiness, foreignBusiness);
        db.Suppliers.AddRange(localSupplier, foreignSupplier);
        db.Bills.AddRange(
            new Bill
            {
                Id = 10,
                Supplier = localSupplier,
                AmountDue = 100,
                DueDate = DateTime.UtcNow.AddDays(-2),
                Status = "unpaid"
            },
            new Bill
            {
                Id = 11,
                Supplier = localSupplier,
                AmountDue = 40,
                DueDate = DateTime.UtcNow.AddDays(2),
                Status = "partial"
            },
            new Bill
            {
                Id = 12,
                Supplier = foreignSupplier,
                AmountDue = 500,
                DueDate = DateTime.UtcNow.AddDays(-1),
                Status = "unpaid"
            },
            new Bill
            {
                Id = 13,
                Supplier = localSupplier,
                AmountDue = 75,
                DueDate = DateTime.UtcNow.AddDays(-1),
                Status = "paid"
            });
        await db.SaveChangesAsync();

        var store = new BillReadStore(db);
        var search = await store.SearchAsync("UNPAID", 10, 1, 10, 17, CancellationToken.None);
        var foreignDetail = await store.GetByIdAsync(12, 17, CancellationToken.None);
        var localSummary = await store.GetSummaryAsync(17, CancellationToken.None);

        Assert.Equal(1, search.TotalCount);
        var bill = Assert.Single(search.Bills);
        Assert.Equal("Local Supplier", bill.SupplierName);
        Assert.True(bill.IsOverdue);
        Assert.Null(foreignDetail);
        Assert.Equal(140, localSummary.TotalUnpaid);
        Assert.Equal(100, localSummary.TotalOverdue);
        Assert.Equal(2, localSummary.UnpaidCount);
        Assert.Equal(1, localSummary.OverdueCount);
    }
}
