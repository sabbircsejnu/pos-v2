using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Customers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Customers;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Customers;

public sealed class CustomerCreateStoreRelationalTests
{
    private static async Task<(SqliteConnection Connection, RetailPOSDbContext Db)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1, 2);
        return (connection, db);
    }

    [Fact]
    public async Task CustomerCreateStore_ChecksUniquenessAndPersistsNormalizedValues()
    {
        var (connection, db) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        db.Customers.Add(new Customer
        {
            Id = 10,
            BusinessId = 1,
            Name = "Existing",
            Phone = "01712345678",
            PhoneNormalized = "+8801712345678",
            Email = "known@example.test"
        });
        await db.SaveChangesAsync();

        var store = new CustomerCreateStore(db, new TestBusinessScope(1));

        var owner = await store.FindByNormalizedPhoneAsync("+8801712345678", CancellationToken.None);
        Assert.Equal(10, owner?.Id);
        Assert.Null(await store.FindByNormalizedPhoneAsync("+8801712345679", CancellationToken.None));
        Assert.True(await store.EmailExistsAsync("KNOWN@example.test", CancellationToken.None));
        Assert.False(await store.EmailExistsAsync("new@example.test", CancellationToken.None));

        var created = await store.CreateAsync(
            new CustomerCreateValues("Ada Lovelace", "01711-000000", "ada@example.test", "+8801711000000"),
            CancellationToken.None);

        Assert.Equal(11, created.Id);
        Assert.Equal("Ada Lovelace", created.Name);
        Assert.Equal("01711-000000", created.Phone);
        Assert.Equal("ada@example.test", created.Email);
        Assert.Equal(0, created.LoyaltyPoints);
        Assert.Equal(0, created.TotalPurchases);
        Assert.NotEqual(default, created.CreatedAt);
        var saved = await db.Customers.SingleAsync(customer => customer.Id == created.Id);
        Assert.True(saved.IsActive);
        Assert.False(saved.IsSystem);
        Assert.Null(saved.CustomerCode);
        Assert.Equal(1, saved.BusinessId);
        Assert.Equal("+8801711000000", saved.PhoneNormalized);
    }

    [Fact]
    public async Task CustomerCreateStore_SamePhoneIsUniquePerBusinessNotGlobally()
    {
        var (connection, db) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        db.Customers.Add(new Customer
        {
            Id = 10,
            BusinessId = 2,
            Name = "Other business customer",
            Phone = "01712345678",
            PhoneNormalized = "+8801712345678"
        });
        await db.SaveChangesAsync();

        var businessOne = new CustomerCreateStore(db, new TestBusinessScope(1));
        var businessTwo = new CustomerCreateStore(db, new TestBusinessScope(2));
        var values = new CustomerCreateValues("Ada", "+880 1712-345678", null, "+8801712345678");

        Assert.Null(await businessOne.FindByNormalizedPhoneAsync("+8801712345678", CancellationToken.None));
        var created = await businessOne.CreateAsync(values, CancellationToken.None);
        Assert.Equal(1, (await db.Customers.SingleAsync(customer => customer.Id == created.Id)).BusinessId);

        var conflict = await Assert.ThrowsAsync<CustomerPhoneConflictException>(
            () => businessOne.CreateAsync(values with { Name = "Duplicate" }, CancellationToken.None));
        Assert.Equal(created.Id, conflict.Existing.Id);

        Assert.Equal(10, (await businessTwo.FindByNormalizedPhoneAsync("+8801712345678", CancellationToken.None))?.Id);
    }
}
