using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Products.Queries;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;

namespace RetailPOS.Tests.Products;

public sealed class ProductVariationsReadStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RetailPOSDbContext _db;
    private readonly GetProductVariationsQueryHandler _handler;

    public ProductVariationsReadStoreTests()
    {
        _connection.Open();
        _db = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(_connection)
                .Options);
        _db.Database.EnsureCreated();
        _handler = new GetProductVariationsQueryHandler(new ProductVariationsReadStore(_db));
    }

    [Fact]
    public async Task Handle_ReturnsOrderedOptionsAndSelectedOptionFallback()
    {
        SeedProductVariations();
        _db.ChangeTracker.Clear();

        var variations = await _handler.Handle(new GetProductVariationsQuery(42), CancellationToken.None);
        var color = Assert.Single(variations, variation => variation.VariationId == 10);
        var size = Assert.Single(variations, variation => variation.VariationId == 20);

        Assert.Equal(new long[] { 102, 101 }, color.Options.Select(option => option.Id));
        Assert.Equal(new long[] { 101 }, color.SelectedOptionIds);
        Assert.Equal(new long[] { 201, 202 }, size.Options.Select(option => option.Id));
        Assert.Equal(new long[] { 201, 202 }, size.SelectedOptionIds);
        Assert.False(color.IsRequired);
        Assert.True(size.IsRequired);
    }

    [Fact]
    public async Task Handle_MissingProductRetainsLegacyError()
    {
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _handler.Handle(new GetProductVariationsQuery(404), CancellationToken.None));

        Assert.Equal("Product with ID 404 not found", exception.Message);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void SeedProductVariations()
    {
        _db.Businesses.Add(new Business { Id = 1, Name = "Test Business 1" });
        _db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "Test", Description = "" });
        _db.Products.Add(new Product { Id = 42, BusinessId = 1, Name = "Widget", CategoryId = 1, BasePrice = 10m });
        _db.Variations.AddRange(
            new Variation { Id = 10, Name = "Color", DisplayOrder = 1 },
            new Variation { Id = 20, Name = "Size", DisplayOrder = 2 });
        _db.VariationOptions.AddRange(
            new VariationOption { Id = 101, VariationId = 10, Name = "Blue", DisplayOrder = 2 },
            new VariationOption { Id = 102, VariationId = 10, Name = "Black", DisplayOrder = 1, IsActive = false },
            new VariationOption { Id = 201, VariationId = 20, Name = "Small", DisplayOrder = 1 },
            new VariationOption { Id = 202, VariationId = 20, Name = "Large", DisplayOrder = 2 });
        _db.ProductVariations.AddRange(
            new ProductVariation { ProductId = 42, VariationId = 10, IsRequired = false },
            new ProductVariation { ProductId = 42, VariationId = 20, IsRequired = true });
        _db.ProductVariationSelectedOptions.Add(
            new ProductVariationSelectedOption { ProductId = 42, VariationId = 10, OptionId = 101 });
        _db.SaveChanges();
    }
}
