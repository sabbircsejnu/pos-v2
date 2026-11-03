using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;

namespace RetailPOS.Tests.Products;

public sealed class ProductSkuStoreRelationalTests
{
    [Fact]
    public async Task SkuLookupChecksProductIdentifiers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db, 1, 2);
        db.Categories.Add(new Category { Id = 42, BusinessId = 1, Name = "Product SKU Category" });
        db.Categories.Add(new Category { Id = 43, BusinessId = 2, Name = "Other business category" });
        db.Products.Add(new Product
        {
            Id = 501,
            BusinessId = 1,
            Name = "Existing Product",
            ProductCode = "EXISTING-001",
            Sku = "EXISTING-SKU",
            CategoryId = 42,
            BasePrice = 25m,
            CostPrice = 10m,
            Status = ProductStatus.Active
        });
        db.Products.Add(new Product
        {
            Id = 502,
            BusinessId = 2,
            Name = "Other business product",
            ProductCode = "OTHER-001",
            Sku = "OTHER-SKU",
            CategoryId = 43,
            BasePrice = 25m,
            CostPrice = 10m,
            Status = ProductStatus.Active
        });
        await db.SaveChangesAsync();

        var store = new ProductSkuStore(db, new TestBusinessScope(1));

        Assert.True(await store.SkuExistsAsync("EXISTING-SKU", CancellationToken.None));
        Assert.False(await store.SkuExistsAsync("UNUSED-SKU", CancellationToken.None));
        Assert.False(await store.SkuExistsAsync("OTHER-SKU", CancellationToken.None));
    }
}
