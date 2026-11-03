using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Customers;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Customers;

public sealed class CustomerReadStoreRelationalTests
{
    [Fact]
    public async Task SearchAndQuickSearch_PreserveFiltersOrderingAndPaging()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1, 2);

        var baseline = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Customers.AddRange(
            Enumerable.Range(1, 4).Select(index => new Customer
            {
                Id = index,
                BusinessId = 1,
                Name = $"Match {index}",
                Phone = $"555-010{index}",
                Email = $"match{index}@example.test",
                CreatedAt = baseline.AddDays(index),
                IsActive = true
            }));
        db.Customers.Add(new Customer
        {
            Id = 5,
            BusinessId = 1,
            Name = "Match inactive",
            CreatedAt = baseline.AddDays(5),
            IsActive = false
        });
        db.Customers.Add(new Customer
        {
            Id = 6,
            BusinessId = 2,
            Name = "Match foreign business",
            CreatedAt = baseline.AddDays(6),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var store = new CustomerReadStore(db, new TestBusinessScope(1));
        var (page, totalCount) = await store.SearchAsync("MATCH", 2, 2, CancellationToken.None);
        var quickResults = await store.QuickSearchAsync("MATCH", 3, CancellationToken.None);

        Assert.Equal(4, totalCount);
        Assert.Equal(["Match 2", "Match 1"], page.Select(customer => customer.Name));
        Assert.Equal(["Match 1", "Match 2", "Match 3"], quickResults.Select(customer => customer.Name));
    }
}
