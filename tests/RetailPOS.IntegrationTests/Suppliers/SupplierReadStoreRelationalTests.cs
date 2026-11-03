using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Suppliers;

namespace RetailPOS.IntegrationTests.Suppliers;

public sealed class SupplierReadStoreRelationalTests
{
    [Fact]
    public async Task ReadStore_PreservesBusinessIsolationSearchPagingAndPerformanceAggregates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var supplierA = new Supplier { Id = 10, BusinessId = 17, Name = "Alpha Supplier", CreditLimit = 500 };
        var supplierB = new Supplier { Id = 11, BusinessId = 17, Name = "Alpine Goods", CreditLimit = 1000 };
        var foreignSupplier = new Supplier { Id = 20, BusinessId = 18, Name = "Alpha Foreign", CreditLimit = 2000 };
        var warehouseA = new Warehouse { Id = 30, BusinessId = 17, Name = "A", Address = "Address" };
        var warehouseB = new Warehouse { Id = 31, BusinessId = 18, Name = "B", Address = "Address" };
        db.Businesses.AddRange(
            new Business { Id = 17, Name = "Business A" },
            new Business { Id = 18, Name = "Business B" });
        db.Suppliers.AddRange(supplierA, supplierB, foreignSupplier);
        db.Warehouses.AddRange(warehouseA, warehouseB);
        db.PurchaseOrders.AddRange(
            new PurchaseOrder
            {
                Id = 100,
                PoNumber = "PO-100",
                Supplier = supplierA,
                Warehouse = warehouseA,
                TotalAmount = 250,
                OrderDate = new DateTime(2026, 1, 1)
            },
            new PurchaseOrder
            {
                Id = 101,
                PoNumber = "PO-101",
                Supplier = supplierA,
                Warehouse = warehouseB,
                TotalAmount = 999,
                OrderDate = new DateTime(2026, 1, 2)
            });
        db.Bills.AddRange(
            new Bill
            {
                Id = 100,
                Supplier = supplierA,
                AmountDue = 125,
                DueDate = new DateTime(2026, 2, 1),
                Status = "unpaid"
            },
            new Bill
            {
                Id = 101,
                Supplier = supplierA,
                AmountDue = 77,
                DueDate = new DateTime(2026, 2, 1),
                Status = "paid"
            });
        await db.SaveChangesAsync();

        var store = new SupplierReadStore(db);
        var all = await store.GetAllAsync(17, CancellationToken.None);
        var search = await store.SearchAsync(
            "al",
            500,
            1000,
            1,
            1,
            "name",
            "DESC",
            17,
            CancellationToken.None);
        var foreignDetail = await store.GetByIdAsync(20, 17, CancellationToken.None);
        var performance = await store.GetPerformanceDataAsync(10, 17, CancellationToken.None);

        Assert.Equal(["Alpha Supplier", "Alpine Goods"], all.Select(supplier => supplier.Name));
        Assert.Equal(2, search.TotalCount);
        Assert.Equal("Alpine Goods", Assert.Single(search.Suppliers).Name);
        Assert.Null(foreignDetail);
        Assert.NotNull(performance);
        Assert.Equal(1, performance.TotalPurchaseOrders);
        Assert.Equal(2, performance.TotalBills);
        Assert.Equal(250, performance.TotalPurchaseAmount);
        Assert.Equal(125, performance.OutstandingBalance);
        Assert.Equal(500, performance.CreditLimit);
    }
}
