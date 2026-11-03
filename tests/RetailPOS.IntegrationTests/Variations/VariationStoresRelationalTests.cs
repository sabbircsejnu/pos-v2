using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Variations;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Variations;
using RetailPOS.Core.Entities;
using RetailPOS.Tests;

namespace RetailPOS.IntegrationTests.Variations;

public sealed class VariationStoresRelationalTests
{
    [Fact]
    public async Task Stores_PreserveCatalogOrderingAndOptionLifecycleWithRelationalPersistence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new RetailPOSDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await TestBusinesses.SeedAsync(db);

        var color = new Variation
        {
            Id = 1,
            Name = "Color",
            DisplayOrder = 1,
            IsActive = true,
            Options =
            [
                new VariationOption { Id = 101, Name = "Blue", DisplayOrder = 2, IsActive = true },
                new VariationOption { Id = 102, Name = "Red", DisplayOrder = 1, IsActive = false }
            ]
        };
        var inactive = new Variation { Id = 2, Name = "Inactive", DisplayOrder = 2, IsActive = false };
        var used = new Variation { Id = 3, Name = "Used", DisplayOrder = 3, IsActive = true };
        var category = new Category { Id = 400, BusinessId = 1, Name = "Test Category" };
        var product = new Product
        {
            Id = 500,
            BusinessId = 1,
            Name = "Used Product",
            CategoryId = category.Id,
            Category = category
        };
        var productVariation = new ProductVariation
        {
            Id = 600,
            ProductId = product.Id,
            Product = product,
            VariationId = used.Id,
            Variation = used
        };
        db.Variations.AddRange(color, inactive, used);
        db.Categories.Add(category);
        db.Products.Add(product);
        db.ProductVariations.Add(productVariation);
        await db.SaveChangesAsync();

        var readStore = new VariationReadStore(db);
        var writeStore = new VariationWriteStore(db);
        var active = await readStore.GetAllAsync(false, CancellationToken.None);
        var all = await readStore.GetAllAsync(true, CancellationToken.None);
        var products = await readStore.GetProductsUsingVariationAsync(3, CancellationToken.None);

        Assert.Equal(["Color", "Used"], active.Select(variation => variation.Name));
        Assert.Equal(["Red", "Blue"], active[0].Options.Select(option => option.Name));
        Assert.False(active[0].Options[0].IsActive);
        Assert.Equal(3, all.Count);
        Assert.Equal("Used Product", Assert.Single(products).Name);
        Assert.True(await writeStore.NameExistsAsync("Color", null, CancellationToken.None));
        Assert.False(await writeStore.NameExistsAsync("color", null, CancellationToken.None));

        var created = await writeStore.CreateAsync(
            new VariationWriteModel(
                "Material",
                4,
                true,
                true,
                [
                    new VariationOptionWriteModel("Cotton", 1.5m, 2, true),
                    new VariationOptionWriteModel("Wool", -2.5m, 1, false)
                ]),
            CancellationToken.None);
        Assert.Equal(["Wool", "Cotton"], created.Options.Select(option => option.Name));
        Assert.Equal("Material", created.Options[0].VariationName);

        var updated = await writeStore.UpdateAsync(
            created.Id,
            new VariationWriteModel("Updated Material", 5, false, false, []),
            CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal("Updated Material", updated.Name);
        Assert.False(updated.IsActive);

        var createdOption = await writeStore.CreateOptionAsync(
            1,
            new VariationOptionWriteModel("Green", 0.75m, 3, true),
            CancellationToken.None);
        Assert.Equal("Color", createdOption.VariationName);
        var updatedOption = await writeStore.UpdateOptionAsync(
            createdOption.Id,
            new VariationOptionWriteModel("Green Updated", 1.25m, 4, false),
            CancellationToken.None);
        Assert.NotNull(updatedOption);
        Assert.Equal("Green Updated", updatedOption.Name);
        Assert.False(updatedOption.IsActive);

        await writeStore.DeleteOptionAsync(createdOption.Id, CancellationToken.None);
        await writeStore.DeleteOptionAsync(999, CancellationToken.None);
        await writeStore.DeleteAsync(created.Id, CancellationToken.None);
        Assert.Null(await readStore.GetByIdAsync(created.Id, CancellationToken.None));
    }
}
