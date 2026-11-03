using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.Tests.Products;

public sealed class ProductDeleteStoreRelationalTests
{
    [Fact]
    public async Task DeleteProduct_RemovesProductAndVariantsTogether()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db);
        db.Categories.Add(new Category { Id = 42, BusinessId = 1, Name = "Product Delete Category" });
        db.Products.Add(new Product
        {
            Id = 501,
            BusinessId = 1,
            Name = "Product To Delete",
            ProductCode = "DELETE-001",
            Sku = "DELETE-SKU",
            CategoryId = 42,
            BasePrice = 25m,
            CostPrice = 10m,
            Status = ProductStatus.Active,
            HasVariants = true
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 601,
            BusinessId = 1,
            ProductId = 501,
            Name = "Blue / Small",
            Sku = "DELETE-VARIANT-SKU",
            Attributes = "{}"
        });
        await db.SaveChangesAsync();

        var scope = new TestBusinessScope(1);
        var readStore = new ProductReadStore(
            db,
            new ProductRepository(db, scope),
            new ProductVariantRepository(db, scope),
            new InventoryRepository(db),
            scope);
        var handler = new DeleteProductCommandHandler(readStore, new ProductDeleteStore(db, scope));

        await handler.Handle(new DeleteProductCommand(501), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.False(await db.Products.AnyAsync(product => product.Id == 501));
        Assert.False(await db.ProductVariants.AnyAsync(variant => variant.ProductId == 501));
    }
}
