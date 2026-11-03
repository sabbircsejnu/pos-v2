using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Outlets;

namespace RetailPOS.IntegrationTests.Outlets;

public sealed class OutletReadStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reads_PreserveBusinessScopeCountsManagerAndSearchBehavior(bool relational)
    {
        await using var db = await CreateDbAsync(relational);
        db.Users.AddRange(
            CreateUser(1, 17, 10, "Manager A"),
            CreateUser(2, 18, 20, "Manager B"),
            CreateUser(3, 17, 10, "Active A"),
            CreateUser(4, 18, 10, "Cross-business"),
            CreateUser(5, 17, 10, "Inactive A", isActive: false));
        await db.SaveChangesAsync();
        db.Outlets.AddRange(
            CreateOutlet(10, 17, "Alpha", "Main Street", "555-0100", 1),
            CreateOutlet(20, 18, "Beta", "North Road", "555-0200", 2));
        await db.SaveChangesAsync();
        var store = new OutletReadStore(db);

        var scoped = await store.GetAllAsync(17, CancellationToken.None);
        var searched = await store.SearchAsync("0100", 17, CancellationToken.None);
        var stats = await store.GetStatsAsync(10, 17, CancellationToken.None);
        var explicitBusinessList = await store.GetByBusinessIdAsync(18, CancellationToken.None);
        Assert.NotNull(await store.GetByIdAsync(10, 17, CancellationToken.None));
        Assert.Null(await store.GetByIdAsync(20, 17, CancellationToken.None));
        Assert.Null(await store.GetStatsAsync(20, 17, CancellationToken.None));

        var outlet = Assert.Single(scoped);
        Assert.Equal("Alpha", outlet.Name);
        Assert.Equal("Manager A", outlet.ManagerName);
        Assert.Equal(2, outlet.UserCount);
        Assert.Equal(10, Assert.Single(searched).Id);
        Assert.Equal((10L, "Alpha", 2, 0, 0m),
            (stats!.Id, stats.Name, stats.UserCount, stats.SalesCount, stats.TotalSales));
        Assert.Equal("Beta", Assert.Single(explicitBusinessList).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnscopedListIncludesAllOutletsInNameOrder(bool relational)
    {
        await using var db = await CreateDbAsync(relational);
        db.Outlets.AddRange(
            CreateOutlet(2, 18, "Zulu", "Second", null, null),
            CreateOutlet(1, 17, "Alpha", "First", null, null));
        await db.SaveChangesAsync();
        var store = new OutletReadStore(db);

        var result = await store.GetAllAsync(null, CancellationToken.None);

        Assert.Equal(["Alpha", "Zulu"], result.Select(outlet => outlet.Name));
    }

    private static async Task<RetailPOSDbContext> CreateDbAsync(bool relational)
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>();
        if (relational)
            options.UseSqlite("Data Source=:memory:;Foreign Keys=False");
        else
            options.UseInMemoryDatabase($"OutletReadStore_{Guid.NewGuid():N}");
        var db = new RetailPOSDbContext(options.Options);
        if (relational)
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();
        }
        return db;
    }

    private static Outlet CreateOutlet(
        long id,
        long businessId,
        string name,
        string address,
        string? contactNumber,
        long? managerId) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            Name = name,
            Address = address,
            ContactNumber = contactNumber,
            ManagerId = managerId
        };

    private static User CreateUser(
        long id,
        long businessId,
        long outletId,
        string name,
        bool isActive = true) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            OutletId = outletId,
            Name = name,
            Email = $"{id}@example.test",
            PasswordHash = "not-used",
            IsActive = isActive
        };
}
