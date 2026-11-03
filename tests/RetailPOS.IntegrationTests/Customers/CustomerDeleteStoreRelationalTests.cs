using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Customers.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Customers;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Customers;

public sealed class CustomerDeleteStoreRelationalTests
{
    [Fact]
    public async Task DeleteCustomer_RemovesCustomerAndNullsHistoricalSaleLink()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db);
        db.Outlets.Add(new Outlet { Id = 1, Name = "Delete Test", Address = "Test" });
        db.Users.Add(new User { Id = 1, Name = "Cashier", Email = "cashier@example.test" });
        db.Customers.Add(new Customer
        {
            Id = 1,
            BusinessId = 1,
            Name = "Customer",
            Sales =
            [
                new Sale
                {
                    Id = 101,
                    OutletId = 1,
                    CashierId = 1,
                    CustomerId = 1,
                    SaleNumber = "S-101",
                    PaymentMethod = "cash"
                }
            ]
        });
        await db.SaveChangesAsync();

        var handler = new DeleteCustomerCommandHandler(new CustomerDeleteStore(db, new TestBusinessScope(1)));

        Assert.True(await handler.Handle(new DeleteCustomerCommand(1), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.False(await db.Customers.AnyAsync(customer => customer.Id == 1));
        Assert.Null((await db.Sales.SingleAsync(sale => sale.Id == 101)).CustomerId);
    }
}
