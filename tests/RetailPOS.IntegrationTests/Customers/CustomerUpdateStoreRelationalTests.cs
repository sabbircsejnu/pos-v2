using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Customers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Customers;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Customers;

public sealed class CustomerUpdateStoreRelationalTests
{
    [Fact]
    public async Task CustomerUpdateStore_ExcludesCurrentCustomerAndPersistsOnlyEditableFields()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1, 2);
        db.Customers.AddRange(
            new Customer
            {
                Id = 1,
                BusinessId = 1,
                Name = "Before",
                Phone = "01712000001",
                PhoneNormalized = "+8801712000001",
                Email = "before@example.test",
                LoyaltyPoints = 20,
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Customer
            {
                Id = 2,
                BusinessId = 1,
                Name = "Other",
                Phone = "01712000002",
                PhoneNormalized = "+8801712000002",
                Email = "other@example.test"
            },
            new Customer
            {
                Id = 3,
                BusinessId = 2,
                Name = "Other business",
                Phone = "01712000003",
                PhoneNormalized = "+8801712000003",
                Email = "foreign@example.test"
            });
        await db.SaveChangesAsync();

        var store = new CustomerUpdateStore(db, new TestBusinessScope(1));
        var snapshot = await store.GetForUpdateAsync(1, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.False(snapshot.IsSystem);
        Assert.Null(await store.FindByNormalizedPhoneAsync("+8801712000001", 1, CancellationToken.None));
        Assert.Equal(2, (await store.FindByNormalizedPhoneAsync("+8801712000002", 1, CancellationToken.None))?.Id);
        Assert.Null(await store.FindByNormalizedPhoneAsync("+8801712000003", 1, CancellationToken.None));
        Assert.False(await store.EmailExistsAsync("BEFORE@example.test", 1, CancellationToken.None));
        Assert.True(await store.EmailExistsAsync("OTHER@example.test", 1, CancellationToken.None));
        Assert.Null(await store.GetForUpdateAsync(3, CancellationToken.None));

        var updated = await store.UpdateAsync(
            new CustomerUpdateValues(1, "After", "01712000101", "after@example.test", "+8801712000101"),
            CancellationToken.None);

        Assert.Equal("After", updated.Name);
        Assert.Equal("01712000101", updated.Phone);
        Assert.Equal("after@example.test", updated.Email);
        db.ChangeTracker.Clear();
        var saved = await db.Customers.SingleAsync(customer => customer.Id == 1);
        Assert.Equal(20, saved.LoyaltyPoints);
        Assert.Equal("+8801712000101", saved.PhoneNormalized);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), saved.CreatedAt);
    }

    [Fact]
    public async Task CustomerUpdateStore_RejectsUpdatingACustomerOfAnotherBusiness()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(connection).Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1, 2);
        db.Customers.Add(new Customer { Id = 3, BusinessId = 2, Name = "Foreign" });
        await db.SaveChangesAsync();

        var store = new CustomerUpdateStore(db, new TestBusinessScope(1));

        await Assert.ThrowsAnyAsync<Exception>(() => store.UpdateAsync(
            new CustomerUpdateValues(3, "Hijacked", null, null),
            CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.Equal("Foreign", (await db.Customers.SingleAsync(customer => customer.Id == 3)).Name);
    }
}
