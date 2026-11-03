using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RetailPOS.Application.Products.Abstractions;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Application.Products.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;

namespace RetailPOS.Tests.Products;

public sealed class ProductCombinationGenerationStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RetailPOSDbContext _db;
    private readonly GenerateProductCombinationsCommandHandler _handler;
    private readonly GenerateSelectedProductCombinationsCommandHandler _selectedHandler;

    public ProductCombinationGenerationStoreTests()
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
        var store = new ProductCombinationGenerationStore(
            _db,
            identifierResolver,
            NullLogger<ProductCombinationGenerationStore>.Instance,
            scope);
        _handler = new GenerateProductCombinationsCommandHandler(store, identifierResolver);
        _selectedHandler = new GenerateSelectedProductCombinationsCommandHandler(
            store,
            identifierResolver);
    }

    [Fact]
    public async Task Handle_CreatesSelectedCombinationsAndSkipsThemOnRetry()
    {
        SeedProductVariations(includeSelectedOptions: true);

        var generated = await _handler.Handle(
            new GenerateProductCombinationsCommand(42),
            CancellationToken.None);

        Assert.Equal(2, generated.Count);
        Assert.Equal("Size: Small / Color: Blue", generated[0].CombinationName);
        Assert.Equal(1m, generated[0].PriceAdjustment);
        Assert.Equal(101m, generated[0].FinalPrice);
        Assert.Equal("TSH-001-SMA-BLU", generated[0].Sku);
        Assert.StartsWith("880001", generated[0].Barcode);
        Assert.Equal(new long[] { 101, 201 }, generated[0].OptionIds);

        var retry = await _handler.Handle(
            new GenerateProductCombinationsCommand(42),
            CancellationToken.None);

        Assert.Empty(retry);
        Assert.Equal(2, await _db.ProductVariants.CountAsync(variant => variant.ProductId == 42));
        Assert.Equal(4, await _db.ProductVariantOptions
            .CountAsync(option => option.Variant.ProductId == 42));
    }

    [Fact]
    public async Task Handle_RollsBackVariantsWhenOptionPersistenceFails()
    {
        SeedProductVariations(includeSelectedOptions: true);
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_generated_variant_option BEFORE INSERT ON product_variant_options " +
            "BEGIN SELECT RAISE(ABORT, 'forced combination generation failure'); END;");

        await Assert.ThrowsAsync<DbUpdateException>(() => _handler.Handle(
            new GenerateProductCombinationsCommand(42),
            CancellationToken.None));

        _db.ChangeTracker.Clear();
        Assert.Empty(await _db.ProductVariants.Where(variant => variant.ProductId == 42).ToListAsync());
        Assert.Empty(await _db.ProductVariantOptions
            .Where(option => option.Variant.ProductId == 42)
            .ToListAsync());
    }

    [Fact]
    public async Task Handle_UsesActiveOptionsWhenNoSelectedOptionRowsExist()
    {
        SeedProductVariations(includeSelectedOptions: false);

        var generated = await _handler.Handle(
            new GenerateProductCombinationsCommand(42),
            CancellationToken.None);

        Assert.Single(generated);
        Assert.Equal(new long[] { 101, 201 }, generated[0].OptionIds);
    }

    [Fact]
    public async Task Handle_PreservesMissingAndEligibilityErrors()
    {
        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(() => _handler.Handle(
            new GenerateProductCombinationsCommand(404),
            CancellationToken.None));
        Assert.Equal("Product with ID 404 not found", missing.Message);

        SeedProduct();
        var noVariations = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(
            new GenerateProductCombinationsCommand(42),
            CancellationToken.None));
        Assert.Equal("No variations assigned to this product", noVariations.Message);
    }

    [Fact]
    public async Task Handle_RejectsAssignedVariationWithoutSelectedOrActiveOptions()
    {
        SeedProductVariations(includeSelectedOptions: false);
        await _db.VariationOptions
            .Where(option => option.VariationId == 10)
            .ExecuteUpdateAsync(update => update.SetProperty(option => option.IsActive, false));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(
            new GenerateProductCombinationsCommand(42),
            CancellationToken.None));

        Assert.Equal(
            "One or more selected variations have no options selected. " +
            "Please select at least one option per variation before generating combinations.",
            exception.Message);
    }

    [Fact]
    public async Task HandleSelected_GeneratesRequestedSubsetAndEmptySelectionMeansAll()
    {
        SeedProductVariations(includeSelectedOptions: true);

        var selected = await _selectedHandler.Handle(
            new GenerateSelectedProductCombinationsCommand(42, [10]),
            CancellationToken.None);

        Assert.Equal(2, selected.Count);
        Assert.All(selected, combination => Assert.Single(combination.OptionIds));

        var all = await _selectedHandler.Handle(
            new GenerateSelectedProductCombinationsCommand(42, []),
            CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.All(all, combination => Assert.Equal(2, combination.OptionIds.Count));
        Assert.Equal(4, await _db.ProductVariants.CountAsync(variant => variant.ProductId == 42));
    }

    [Fact]
    public async Task HandleSelected_RejectsUnassignedVariationBeforeGeneration()
    {
        SeedProductVariations(includeSelectedOptions: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _selectedHandler.Handle(
                new GenerateSelectedProductCombinationsCommand(42, [99]),
                CancellationToken.None));

        Assert.Equal(
            "Variation ID(s) 99 are not assigned to product 42. Assigned variation IDs are: 10, 20.",
            exception.Message);
        Assert.Empty(await _db.ProductVariants.Where(variant => variant.ProductId == 42).ToListAsync());
    }

    [Fact]
    public async Task ReadStore_ProjectsCombinationsAndReturnsEmptyForProductWithoutVariants()
    {
        SeedProductVariations(includeSelectedOptions: true);
        var readStore = new ProductCombinationsReadStore(_db);

        var initiallyEmpty = await readStore.GetByProductIdAsync(42, CancellationToken.None);
        Assert.NotNull(initiallyEmpty);
        Assert.Empty(initiallyEmpty);

        var generated = await _handler.Handle(
            new GenerateProductCombinationsCommand(42),
            CancellationToken.None);
        var projected = await readStore.GetByProductIdAsync(42, CancellationToken.None);

        Assert.Equal(generated.Count, projected!.Count);
        Assert.Equal(generated[0].Id, projected[0].Id);
        Assert.Equal(generated[0].Sku, projected[0].Sku);
        Assert.Equal(generated[0].FinalPrice, projected[0].FinalPrice);
        Assert.Equal(generated[0].OptionIds, projected[0].OptionIds);
        Assert.Equal(generated[0].Options[0].OptionName, projected[0].Options[0].OptionName);
    }

    [Fact]
    public async Task ReadStore_ReturnsNullForMissingProduct()
    {
        var readStore = new ProductCombinationsReadStore(_db);

        Assert.Null(await readStore.GetByProductIdAsync(404, CancellationToken.None));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void SeedProductVariations(bool includeSelectedOptions)
    {
        SeedProduct();
        _db.Variations.AddRange(
            new Variation { Id = 10, Name = "Size" },
            new Variation { Id = 20, Name = "Color" });
        _db.VariationOptions.AddRange(
            new VariationOption { Id = 101, VariationId = 10, Name = "Small", PriceAdjustment = -2m, DisplayOrder = 1, IsActive = true },
            new VariationOption { Id = 102, VariationId = 10, Name = "Large", PriceAdjustment = 4m, DisplayOrder = 2, IsActive = false },
            new VariationOption { Id = 201, VariationId = 20, Name = "Blue", PriceAdjustment = 3m, DisplayOrder = 1, IsActive = true },
            new VariationOption { Id = 202, VariationId = 20, Name = "Red", PriceAdjustment = 5m, DisplayOrder = 2, IsActive = false });
        _db.ProductVariations.AddRange(
            new ProductVariation { ProductId = 42, VariationId = 10 },
            new ProductVariation { ProductId = 42, VariationId = 20 });

        if (includeSelectedOptions)
        {
            _db.ProductVariationSelectedOptions.AddRange(
                new ProductVariationSelectedOption { ProductId = 42, VariationId = 10, OptionId = 101 },
                new ProductVariationSelectedOption { ProductId = 42, VariationId = 10, OptionId = 102 },
                new ProductVariationSelectedOption { ProductId = 42, VariationId = 20, OptionId = 201 });
        }

        _db.SaveChanges();
    }

    private void SeedProduct()
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
        _db.SaveChanges();
    }
}
