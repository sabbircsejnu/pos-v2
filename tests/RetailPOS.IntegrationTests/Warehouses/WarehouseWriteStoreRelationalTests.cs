using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Application.Integration;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Warehouses;
using Moq;

namespace RetailPOS.Tests.Warehouses;

public sealed class WarehouseWriteStoreRelationalTests
{
    [Fact]
    public async Task WarehouseWriteStore_PreservesBusinessGuardsUpdatesAndDeleteScope()
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
            Manager = managerA
        };
        var warehouseB = new Warehouse
        {
            Id = 20,
            Business = businessB,
            Name = "Beta",
            Address = "North Road",
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

        var store = new WarehouseWriteStore(db, Mock.Of<IOperationalLocationSourceEventWriter>());
        Assert.True(await store.ExistsByNameAsync("alpha", null, 17, CancellationToken.None));
        Assert.False(await store.ExistsByNameAsync("Beta", null, 17, CancellationToken.None));
        Assert.True(await store.ManagerExistsAsync(1, 17, CancellationToken.None));
        Assert.False(await store.ManagerExistsAsync(2, 17, CancellationToken.None));
        Assert.Equal(1, await store.GetPurchaseOrderCountAsync(10, 17, CancellationToken.None));
        Assert.Equal(0, await store.GetPurchaseOrderCountAsync(10, 18, CancellationToken.None));

        var created = await store.CreateAsync(
            "Gamma",
            "New Street",
            50,
            1,
            17,
            CancellationToken.None);
        Assert.Equal(17, (await db.Warehouses.SingleAsync(item => item.Id == created.Id)).BusinessId);
        Assert.Equal("Manager A", created.ManagerName);
        Assert.Equal(0, created.PurchaseOrderCount);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        Assert.Null(await store.UpdateAsync(
            warehouseB.Id,
            "Should Not Move",
            "Address",
            20,
            null,
            17,
            CancellationToken.None));
        var updated = await store.UpdateAsync(
            created.Id,
            "Gamma Updated",
            "Updated Street",
            75,
            1,
            17,
            CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal("Gamma Updated", updated.Name);
        Assert.Equal(75, updated.Capacity);
        Assert.Equal("Manager A", updated.ManagerName);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        Assert.False(await store.DeleteAsync(created.Id, 18, CancellationToken.None));
        Assert.True(await store.DeleteAsync(created.Id, 17, CancellationToken.None));
        Assert.False(await store.DeleteAsync(created.Id, 17, CancellationToken.None));
    }
}
