using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.Tests.Products;

public sealed class ProductCreateStoreRelationalTests
{
    [Fact]
    public async Task CreateProductAndVariants_CommitTogether()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db);
        db.Categories.Add(new Category { Id = 42, BusinessId = 1, Name = "Product Create Category" });
        await db.SaveChangesAsync();

        var scope = new TestBusinessScope(1);
        var lookupStore = new ProductIdentifierLookupStore(db, scope);
        var resolver = new ProductIdentifierResolver(lookupStore);
        var store = new ProductCreateStore(db, resolver, scope);
        var readStore = CreateReadStore(db);
        var handler = new CreateProductCommandHandler(store, readStore);

        var created = await handler.Handle(
            CreateCommand(
                "Relational Product",
                "REL-001",
                [
                    new ProductVariantCreation(
                        "Blue-Small",
                        null,
                        null,
                        "{\"color\":\"Blue\",\"size\":\"Small\"}",
                        2m)
                ]),
            CancellationToken.None);

        Assert.Equal("Relational Product", created.Name);
        Assert.Equal("REL-001", created.ProductCode);
        Assert.Single(created.Variants);
        Assert.Equal(42, created.CategoryId);
        Assert.Equal(1, await db.Products.CountAsync());
        Assert.Equal(1, await db.ProductVariants.CountAsync());
    }

    [Fact]
    public async Task DuplicateVariantIdentifier_RollsBackProductAndPreviouslySavedVariants()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db);
        db.Categories.Add(new Category { Id = 42, BusinessId = 1, Name = "Product Create Category" });
        await db.SaveChangesAsync();

        var scope = new TestBusinessScope(1);
        var lookupStore = new ProductIdentifierLookupStore(db, scope);
        var resolver = new ProductIdentifierResolver(lookupStore);
        var store = new ProductCreateStore(db, resolver, scope);
        var handler = new CreateProductCommandHandler(store, CreateReadStore(db));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                CreateCommand(
                    "Product With Duplicate Variant SKU",
                    "REL-002",
                    [
                        new ProductVariantCreation("Blue-Small", "DUPLICATE", "1234567890123", "{}", 0m),
                        new ProductVariantCreation("Red-Large", "DUPLICATE", "1234567890124", "{}", 0m)
                    ]),
                CancellationToken.None));

        Assert.Equal("Variant SKU 'DUPLICATE' already exists", exception.Message);
        db.ChangeTracker.Clear();
        Assert.False(await db.Products.AnyAsync(product =>
            product.Name == "Product With Duplicate Variant SKU"));
        Assert.Empty(await db.ProductVariants.ToListAsync());
    }

    private static CreateProductCommand CreateCommand(
        string name,
        string productCode,
        IReadOnlyList<ProductVariantCreation> variants) =>
        new(
            name,
            "Relational create characterization",
            productCode,
            null,
            null,
            42,
            30m,
            12m,
            5m,
            null,
            "draft",
            true,
            variants);

    private static ProductReadStore CreateReadStore(RetailPOSDbContext db)
    {
        var scope = new TestBusinessScope(1);
        return new(
            db,
            new ProductRepository(db, scope),
            new ProductVariantRepository(db, scope),
            new InventoryRepository(db),
            scope);
    }
}
