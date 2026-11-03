using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Customers;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Customers;

public sealed class CustomerLoyaltyStoreRelationalTests
{
    [Fact]
    public async Task LoyaltyStore_AddsPointsAndPersistsCustomerState()
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
            LoyaltyPoints = 7,
            Email = "loyal@example.test"
        });
        await db.SaveChangesAsync();

        var store = new CustomerLoyaltyStore(db, new TestBusinessScope(1));
        var snapshot = await store.GetForLoyaltyUpdateAsync(1, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal(7, snapshot.LoyaltyPoints);

        var result = await store.SetLoyaltyPointsAsync(1, 12, CancellationToken.None);

        Assert.Equal(12, result.LoyaltyPoints);
        db.ChangeTracker.Clear();
        var saved = await db.Customers.SingleAsync();
        Assert.Equal(12, saved.LoyaltyPoints);
        Assert.Equal("Loyal Customer", saved.Name);
        Assert.Equal("loyal@example.test", saved.Email);
    }
}
