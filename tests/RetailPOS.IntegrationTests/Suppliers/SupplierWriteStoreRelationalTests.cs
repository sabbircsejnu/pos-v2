using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Suppliers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Suppliers;

namespace RetailPOS.IntegrationTests.Suppliers;

public sealed class SupplierWriteStoreRelationalTests
{
    [Fact]
    public async Task WriteStore_PreservesBusinessScopedUniquenessUpdateAndDeleteBehavior()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var business = new Business { Id = 17, Name = "Business A" };
        var supplier = new Supplier { Id = 10, BusinessId = 17, Name = "Alpha Supplier", CreditLimit = 500 };
        var warehouse = new Warehouse { Id = 30, BusinessId = 17, Name = "Warehouse", Address = "Address" };
        db.Businesses.Add(business);
        db.Suppliers.Add(supplier);
        db.Warehouses.Add(warehouse);
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 100,
            PoNumber = "PO-100",
            Supplier = supplier,
            Warehouse = warehouse,
            TotalAmount = 250,
            OrderDate = new DateTime(2026, 1, 1)
        });
        db.Bills.Add(new Bill
        {
            Id = 100,
            Supplier = supplier,
            AmountDue = 100,
            DueDate = new DateTime(2026, 2, 1),
            Status = "unpaid"
        });
        await db.SaveChangesAsync();

        var store = new SupplierWriteStore(db);
        Assert.True(await store.NameExistsAsync("alpha supplier", 17, null, CancellationToken.None));
        Assert.False(await store.NameExistsAsync("Alpha Supplier", 18, null, CancellationToken.None));
        Assert.Equal(1, await store.GetPurchaseOrderCountAsync(10, 17, CancellationToken.None));
        Assert.Equal(1, await store.GetBillCountAsync(10, 17, CancellationToken.None));

        var created = await store.CreateAsync(
            new SupplierWriteModel("Beta Supplier", "555-0100", "Second address", 700),
            17,
            CancellationToken.None);
        Assert.Equal(17, (await db.Suppliers.SingleAsync(candidate => candidate.Id == created.Id)).BusinessId);

        var updated = await store.UpdateAsync(
            10,
            new SupplierWriteModel("Updated Supplier", "555-0199", "Updated address", 900),
            17,
            CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal("Updated Supplier", updated.Name);
        Assert.Equal(900, updated.CreditLimit);

        Assert.False(await store.DeleteAsync(10, 18, CancellationToken.None));
        Assert.True(await store.DeleteAsync(created.Id, 17, CancellationToken.None));
        Assert.Null(await db.Suppliers.FindAsync(created.Id));
    }
}
