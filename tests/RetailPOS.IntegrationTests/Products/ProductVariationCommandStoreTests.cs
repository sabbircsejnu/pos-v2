using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Commands;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Products;

public sealed class ProductVariationCommandStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RetailPOSDbContext _db;
    private readonly AssignProductVariationsCommandHandler _handler;

    public ProductVariationCommandStoreTests()
    {
        _connection.Open();
        _db = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options);
        _db.Database.EnsureCreated();
        _handler = new AssignProductVariationsCommandHandler(new ProductVariationCommandStore(_db));
    }

    [Fact]
    public async Task Handle_ReplacesAssignmentsAndUsesProvidedOrActiveDefaultOptions()
    {
        SeedProductVariations();
        _db.ChangeTracker.Clear();

        await AssignAsync(
            [10, 20],
            new Dictionary<long, List<long>> { [10] = [102] });

        _db.ChangeTracker.Clear();
        var assignments = await _db.ProductVariations
            .Where(assignment => assignment.ProductId == 42)
            .OrderBy(assignment => assignment.VariationId)
            .ToListAsync();
        Assert.Equal(new long[] { 10, 20 }, assignments.Select(assignment => assignment.VariationId));
        Assert.True(assignments[0].IsRequired);
        Assert.False(assignments[1].IsRequired);

        var selected = await _db.ProductVariationSelectedOptions
            .Where(option => option.ProductId == 42)
            .Select(option => new { option.VariationId, option.OptionId })
            .ToListAsync();
        Assert.Equal(
            new[] { (10L, 102L), (20L, 201L) },
            selected.OrderBy(option => option.VariationId)
                .Select(option => (option.VariationId, option.OptionId)));
        Assert.True((await _db.Products.SingleAsync(product => product.Id == 42)).HasVariants);

        await AssignAsync([], new Dictionary<long, List<long>>());
        _db.ChangeTracker.Clear();
        Assert.Empty(await _db.ProductVariations.Where(row => row.ProductId == 42).ToListAsync());
        Assert.Empty(await _db.ProductVariationSelectedOptions.Where(row => row.ProductId == 42).ToListAsync());
        Assert.False((await _db.Products.SingleAsync(product => product.Id == 42)).HasVariants);
    }

    [Fact]
    public async Task Handle_RollsBackAssignmentAndSelectedOptionsWhenReplacementFails()
    {
        SeedProductVariations();
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_selected_option_insert BEFORE INSERT ON product_variation_selected_options " +
            "BEGIN SELECT RAISE(ABORT, 'forced variation assignment failure'); END;");
        _db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<DbUpdateException>(
            () => AssignAsync([20], new Dictionary<long, List<long>>()));

        _db.ChangeTracker.Clear();
        var assignments = await _db.ProductVariations
            .Where(assignment => assignment.ProductId == 42)
            .ToListAsync();
        Assert.Equal(new long[] { 10 }, assignments.Select(assignment => assignment.VariationId));
        Assert.True(Assert.Single(assignments).IsRequired);
        var selected = Assert.Single(await _db.ProductVariationSelectedOptions
            .Where(option => option.ProductId == 42)
            .ToListAsync());
        Assert.Equal(101, selected.OptionId);
        Assert.False((await _db.Products.SingleAsync(product => product.Id == 42)).HasVariants);
    }

    [Fact]
    public async Task Handle_MissingProductRetainsLegacyError()
    {
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => AssignAsync([10], new Dictionary<long, List<long>>(), productId: 404));

        Assert.Equal("Product with ID 404 not found", exception.Message);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Task AssignAsync(
        IReadOnlyList<long> variationIds,
        IReadOnlyDictionary<long, List<long>> selectedOptionsByVariation,
        long productId = 42) =>
        _handler.Handle(
            new AssignProductVariationsCommand(productId, variationIds, selectedOptionsByVariation),
            CancellationToken.None);

    private void SeedProductVariations()
    {
        _db.Businesses.Add(new Business { Id = 1, Name = "Test Business 1" });
        _db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "Test", Description = "" });
        _db.Products.Add(new Product { Id = 42, BusinessId = 1, Name = "Widget", CategoryId = 1, BasePrice = 10m });
        _db.Variations.AddRange(
            new Variation { Id = 10, Name = "Color" },
            new Variation { Id = 20, Name = "Size" });
        _db.VariationOptions.AddRange(
            new VariationOption { Id = 101, VariationId = 10, Name = "Blue", IsActive = true },
            new VariationOption { Id = 102, VariationId = 10, Name = "Black", IsActive = false },
            new VariationOption { Id = 201, VariationId = 20, Name = "Small", IsActive = true },
            new VariationOption { Id = 202, VariationId = 20, Name = "Large", IsActive = false });
        _db.ProductVariations.Add(new ProductVariation
        {
            ProductId = 42,
            VariationId = 10,
            IsRequired = true
        });
        _db.ProductVariationSelectedOptions.Add(new ProductVariationSelectedOption
        {
            ProductId = 42,
            VariationId = 10,
            OptionId = 101
        });
        _db.SaveChanges();
    }
}
