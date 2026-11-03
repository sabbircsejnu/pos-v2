using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;

namespace RetailPOS.Tests.Products;

public sealed class ProductManualCombinationStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RetailPOSDbContext _db;
    private readonly CreateManualProductCombinationCommandHandler _handler;

    public ProductManualCombinationStoreTests()
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

        IProductIdentifierLookupStore lookupStore = new ProductIdentifierLookupStore(_db, scope);
        var identifierResolver = new ProductIdentifierResolver(lookupStore);
        _handler = new CreateManualProductCombinationCommandHandler(
            new ProductManualCombinationStore(_db, scope),
            identifierResolver);
    }

    [Fact]
    public async Task Handle_CreatesCombinationAndRejectsSequentialDuplicate()
    {
        SeedProductWithOptions();
        var command = new CreateManualProductCombinationCommand(
            42,
            [201, 101],
            " manual sku ",
            "8801010000019",
            10m,
            4m);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal("MANUAL-SKU", result.Sku);
        Assert.Equal("8801010000019", result.Barcode);
        Assert.Equal("Blue - Small", result.CombinationName);
        Assert.Equal(11m, result.PriceAdjustment);
        Assert.Equal(4m, result.CostAdjustment);
        Assert.Equal(111m, result.FinalPrice);
        Assert.Equal(new long[] { 201, 101 }, result.OptionIds);
        Assert.Equal(new long[] { 101, 201 }, result.Options.Select(option => option.OptionId));
        Assert.Equal(2, await _db.ProductVariantOptions
            .CountAsync(option => option.Variant.ProductId == 42));

        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(command with { Sku = "DIFFERENT-SKU" }, CancellationToken.None));
        Assert.Equal("This combination already exists", duplicate.Message);
        Assert.Single(await _db.ProductVariants.Where(variant => variant.ProductId == 42).ToListAsync());
    }

    [Fact]
    public async Task Handle_RollsBackVariantWhenOptionLinkInsertFails()
    {
        SeedProductWithOptions();
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_manual_variant_option BEFORE INSERT ON product_variant_options " +
            "BEGIN SELECT RAISE(ABORT, 'forced manual combination failure'); END;");

        await Assert.ThrowsAsync<DbUpdateException>(() => _handler.Handle(
            new CreateManualProductCombinationCommand(
                42,
                [101, 201],
                "SKU-1",
                "8801010000019",
                0m,
                0m),
            CancellationToken.None));

        _db.ChangeTracker.Clear();
        Assert.Empty(await _db.ProductVariants.Where(variant => variant.ProductId == 42).ToListAsync());
        Assert.Empty(await _db.ProductVariantOptions
            .Where(option => option.Variant.ProductId == 42)
            .ToListAsync());
    }

    [Fact]
    public async Task Handle_PreservesMissingProductAndOptionErrors()
    {
        var missingProduct = await Assert.ThrowsAsync<KeyNotFoundException>(() => _handler.Handle(
            new CreateManualProductCombinationCommand(404, [101], null, null, 0m, 0m),
            CancellationToken.None));
        Assert.Equal("Product with ID 404 not found", missingProduct.Message);

        SeedProductWithOptions();
        var missingOption = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(
            new CreateManualProductCombinationCommand(42, [101, 404], null, null, 0m, 0m),
            CancellationToken.None));
        Assert.Equal("Some options not found", missingOption.Message);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void SeedProductWithOptions()
    {
        _db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "Test", Description = "" });
        _db.Products.Add(new Product
        {
            Id = 42,
            BusinessId = 1,
            Name = "Widget",
            ProductCode = "TSH-001",
            CategoryId = 1,
            BasePrice = 100m
        });
        _db.Variations.AddRange(
            new Variation { Id = 10, Name = "Color" },
            new Variation { Id = 20, Name = "Size" });
        _db.VariationOptions.AddRange(
            new VariationOption { Id = 101, VariationId = 10, Name = "Blue", PriceAdjustment = 3m },
            new VariationOption { Id = 201, VariationId = 20, Name = "Small", PriceAdjustment = -2m });
        _db.SaveChanges();
    }
}
