using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Warehouses;

namespace RetailPOS.Tests.Warehouses;

public sealed class WarehouseReadStoreRelationalTests
{
    [Fact]
    public async Task WarehouseReadStore_PreservesBusinessFiltersProjectionsAndStats()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var businessA = new Business { Id = 17, Name = "A" };
        var businessB = new Business { Id = 18, Name = "B" };
        var managerA = new User
        {
            Id = 1,
            Business = businessA,
            Name = "Manager A",
            Email = "manager-a@example.test",
            PasswordHash = "unused"
        };
        var managerB = new User
        {
            Id = 2,
            Business = businessB,
            Name = "Manager B",
            Email = "manager-b@example.test",
            PasswordHash = "unused"
        };
        var warehouseA = new Warehouse
        {
            Id = 10,
            Business = businessA,
            Name = "Alpha",
            Address = "Main Street",
            Capacity = 100,
            Manager = managerA
        };
        var warehouseB = new Warehouse
        {
            Id = 20,
            Business = businessB,
            Name = "Beta",
            Address = "North Road",
            Capacity = 200,
            Manager = managerB
        };
        var supplier = new Supplier
        {
            Id = 1,
            Business = businessA,
            Name = "Supplier A"
        };
        db.Businesses.AddRange(businessA, businessB);
        db.Warehouses.AddRange(warehouseA, warehouseB);
        db.Suppliers.Add(supplier);
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 100,
            PoNumber = "PO-100",
            Supplier = supplier,
            Warehouse = warehouseA,
            OrderDate = new DateTime(2026, 1, 1)
        });
        await db.SaveChangesAsync();

        var store = new WarehouseReadStore(db);
        var businessWarehouses = await store.GetAllAsync(17, null, CancellationToken.None);
        var search = await store.GetAllAsync(17, "Main", CancellationToken.None);
        var explicitBusinessWarehouses = await store.GetByBusinessIdAsync(18, CancellationToken.None);
        var foreignDetail = await store.GetByIdAsync(20, 17, CancellationToken.None);
        var stats = await store.GetStatsAsync(10, 17, CancellationToken.None);

        var warehouse = Assert.Single(businessWarehouses);
        Assert.Equal("Alpha", warehouse.Name);
        Assert.Equal("Manager A", warehouse.ManagerName);
        Assert.Equal(1, warehouse.PurchaseOrderCount);
        Assert.Equal(10, Assert.Single(search).Id);
        Assert.Equal("Beta", Assert.Single(explicitBusinessWarehouses).Name);
        Assert.Null(foreignDetail);
        Assert.NotNull(stats);
        Assert.Equal(100, stats.Capacity);
        Assert.Equal(1, stats.PurchaseOrderCount);
        Assert.Equal(0, stats.CurrentStock);
        Assert.Equal(0, stats.CapacityUtilization);
    }
}
