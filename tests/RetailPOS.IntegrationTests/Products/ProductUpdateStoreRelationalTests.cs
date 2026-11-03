using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.Tests.Products;

public sealed class ProductUpdateStoreRelationalTests
{
    [Fact]
    public async Task UpdateProduct_PersistsFieldsAndPreservesVariants()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db);
        db.Categories.Add(new Category { Id = 42, BusinessId = 1, Name = "Product Update Category" });
        db.Products.Add(new Product
        {
            Id = 501,
            BusinessId = 1,
            Name = "Before Update",
            ProductCode = "BEFORE-001",
            Sku = "BEFORE-SKU",
            Barcode = "BEFORE-BARCODE",
            CategoryId = 42,
            BasePrice = 25m,
            CostPrice = 10m,
            TaxRate = 5m,
            HasVariants = true,
            Status = ProductStatus.Active
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 601,
            BusinessId = 1,
            ProductId = 501,
            Name = "Blue / Small",
            Sku = "VARIANT-SKU",
            Attributes = "{\"color\":\"Blue\",\"size\":\"S\"}",
            PriceAdjustment = 2m
        });
        await db.SaveChangesAsync();

        var scope = new TestBusinessScope(1);
        var store = new ProductUpdateStore(db, scope);
        var readStore = new ProductReadStore(
            db,
            new ProductRepository(db, scope),
            new ProductVariantRepository(db, scope),
            new InventoryRepository(db),
            scope);
        var handler = new UpdateProductCommandHandler(store, readStore);

        var updated = await handler.Handle(
            new UpdateProductCommand(
                501,
                "After Update",
                "Updated description",
                " after-002 ",
                null,
                null,
                42,
                30m,
                null,
                8m,
                null,
                "inactive"),
            CancellationToken.None);

        Assert.Equal("After Update", updated.Name);
        Assert.Equal("AFTER-002", updated.ProductCode);
        Assert.Equal("BEFORE-SKU", updated.Sku);
        Assert.Null(updated.Barcode);
        Assert.Equal(0m, updated.CostPrice);
        Assert.Equal("inactive", updated.Status);
        Assert.Single(updated.Variants);
        Assert.Equal("VARIANT-SKU", updated.Variants[0].Sku);

        db.ChangeTracker.Clear();
        var saved = await db.Products.SingleAsync(product => product.Id == 501);
        Assert.Equal("AFTER-002", saved.ProductCode);
        Assert.Equal("BEFORE-SKU", saved.Sku);
        Assert.Equal("VARIANT-SKU", (await db.ProductVariants.SingleAsync()).Sku);
    }
}
