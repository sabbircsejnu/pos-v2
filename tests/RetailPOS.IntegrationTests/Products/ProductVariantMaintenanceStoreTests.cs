using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;

namespace RetailPOS.Tests.Products;

public sealed class ProductVariantMaintenanceStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RetailPOSDbContext _db;
    private readonly IProductVariantMaintenanceStore _store;
    private readonly IProductIdentifierResolver _identifierResolver;

    public ProductVariantMaintenanceStoreTests()
    {
        _connection.Open();
        _db = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options);
        _db.Database.EnsureCreated();
        _db.Businesses.Add(new Business { Id = 1, Name = "Test Business" });
        _db.SaveChanges();
        var scope = new TestBusinessScope(1);
        _store = new ProductVariantMaintenanceStore(_db, scope);
        _identifierResolver = new ProductIdentifierResolver(new ProductIdentifierLookupStore(_db, scope));
    }

    [Fact]
    public async Task UpdateCommand_PreservesOwnIdentifiersAndPersistsAdjustments()
    {
        SeedVariant(withOption: true, barcode: "8801234567890");
        var originalUpdatedAt = (await _db.ProductVariants.SingleAsync()).UpdatedAt;
        var handler = new UpdateProductVariantCommandHandler(_store, _identifierResolver);

        await handler.Handle(
            new UpdateProductVariantCommand(1, " sku-one ", "8801234567890", 8m, 3m),
            CancellationToken.None);

        var variant = await _db.ProductVariants.AsNoTracking().SingleAsync();
        Assert.Equal("SKU-ONE", variant.Sku);
        Assert.Equal("8801234567893", variant.Barcode);
        Assert.Equal(8m, variant.PriceAdjustment);
        Assert.Equal(3m, variant.CostAdjustment);
        Assert.True(variant.UpdatedAt >= originalUpdatedAt);
    }

    [Fact]
    public async Task DeleteCommand_RemovesVariantInventoryAndOptionLinks()
    {
        SeedVariant(withOption: true);
        await SeedInventoryAsync();

        await new DeleteProductVariantCommandHandler(_store).Handle(
            new DeleteProductVariantCommand(1),
            CancellationToken.None);

        _db.ChangeTracker.Clear();
        Assert.Empty(await _db.ProductVariants.ToListAsync());
        Assert.Empty(await _db.Inventories.ToListAsync());
        Assert.Empty(await _db.ProductVariantOptions.ToListAsync());
    }

    [Fact]
    public async Task DeleteCommand_RollsBackInventoryWhenVariantDeleteFails()
    {
        SeedVariant(withOption: false);
        await SeedInventoryAsync();
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_variant_delete BEFORE DELETE ON product_variants " +
            "BEGIN SELECT RAISE(ABORT, 'forced variant delete failure'); END;");

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new DeleteProductVariantCommandHandler(_store).Handle(
                new DeleteProductVariantCommand(1),
                CancellationToken.None));

        _db.ChangeTracker.Clear();
        Assert.Single(await _db.ProductVariants.ToListAsync());
        Assert.Single(await _db.Inventories.ToListAsync());
    }

    [Fact]
    public async Task DeleteCommand_PreservesMissingVariantError()
    {
        var error = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new DeleteProductVariantCommandHandler(_store).Handle(
                new DeleteProductVariantCommand(404),
                CancellationToken.None));

        Assert.Equal("Variant with ID 404 not found", error.Message);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void SeedVariant(bool withOption, string? barcode = null)
    {
        _db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "Test", Description = "" });
        _db.Products.Add(new Product
        {
            Id = 1,
            BusinessId = 1,
            Name = "Widget",
            ProductCode = "WID",
            CategoryId = 1,
            BasePrice = 100m
        });
        _db.ProductVariants.Add(new ProductVariant
        {
            Id = 1,
            BusinessId = 1,
            ProductId = 1,
            Name = "Blue / Small",
            Sku = "SKU-ONE",
            Barcode = barcode,
            PriceAdjustment = 2m
        });

        if (withOption)
        {
            _db.Variations.Add(new Variation { Id = 1, Name = "Color" });
            _db.VariationOptions.Add(new VariationOption { Id = 1, VariationId = 1, Name = "Blue" });
            _db.ProductVariantOptions.Add(new ProductVariantOption { VariantId = 1, OptionId = 1 });
        }

        _db.SaveChanges();
    }

    private Task<int> SeedInventoryAsync() =>
        _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO inventories (id, variant_id, location_id, location_type, quantity, low_stock_threshold, xmin) " +
            "VALUES (1, 1, 5, 'outlet', 4, 10, 1)");
}
