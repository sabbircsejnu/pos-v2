using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Customers.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Customers;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Customers;

public sealed class CustomerLoyaltyRedemptionRelationalTests
{
    [Fact]
    public async Task RedeemPoints_PersistsReducedBalance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db);
        db.Customers.Add(new Customer
        {
            Id = 1,
            BusinessId = 1,
            Name = "Loyal Customer",
            LoyaltyPoints = 10
        });
        await db.SaveChangesAsync();

        var handler = new RedeemCustomerLoyaltyPointsCommandHandler(new CustomerLoyaltyStore(db, new TestBusinessScope(1)));
        var result = await handler.Handle(
            new RedeemCustomerLoyaltyPointsCommand(1, 4),
            CancellationToken.None);

        Assert.Equal(6, result.LoyaltyPoints);
        db.ChangeTracker.Clear();
        Assert.Equal(6, (await db.Customers.SingleAsync()).LoyaltyPoints);
    }
}
