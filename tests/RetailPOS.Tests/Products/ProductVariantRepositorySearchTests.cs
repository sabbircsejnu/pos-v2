using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.Tests.Products;

public class ProductVariantRepositorySearchTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly ProductVariantRepository _repo;

    public ProductVariantRepositorySearchTests()
    {
        var opts = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(databaseName: $"variant-search-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new RetailPOSDbContext(opts);
        _repo = new ProductVariantRepository(_db);

        SeedData();
    }

    [Fact]
    public async Task SearchAsync_FindsByVariantSku()
    {
        var result = (await _repo.SearchAsync("TSH-RED-S", 1, 20)).ToList();

        Assert.NotEmpty(result);
        Assert.Contains(result, v => v.Sku == "TSH-RED-S");
    }

    [Fact]
    public async Task SearchAsync_FindsAllActiveVariantsByExactMainProductCode()
    {
        var result = (await _repo.SearchAsync("TSH-001", 1, 20)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, v => Assert.Equal(1001, v.ProductId));
    }

    [Fact]
    public async Task SearchAsync_FindsAllActiveVariantsByPartialMainProductCode()
    {
        var result = (await _repo.SearchAsync("TSH", 1, 20)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, v => Assert.Equal(1001, v.ProductId));
    }

    [Fact]
    public async Task SearchAsync_FindsByProductName()
    {
        var result = (await _repo.SearchAsync("T-Shirt", 1, 20)).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, v => Assert.Equal(1001, v.ProductId));
    }

    [Fact]
    public async Task SearchAsync_FindsByVariantAttributes()
    {
        var result = (await _repo.SearchAsync("Crimson", 1, 20)).ToList();

        Assert.Single(result);
        Assert.Equal("TSH-RED-S", result[0].Sku);
    }

    [Fact]
    public async Task SearchAsync_FindsByVariantBarcode()
    {
        var result = (await _repo.SearchAsync("111111", 1, 20)).ToList();

        Assert.Single(result);
        Assert.Equal("TSH-RED-S", result[0].Sku);
    }

    [Fact]
    public async Task SearchAsync_ExcludesInactiveProducts()
    {
        var result = (await _repo.SearchAsync("SNK-999", 1, 20)).ToList();

        Assert.Empty(result);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedData()
    {
        _db.Categories.Add(new Category
        {
            Id = 501,
            Name = "Apparel",
            Description = "Test category"
        });

        _db.Products.AddRange(
            new Product
            {
                Id = 1001,
                Name = "Classic T-Shirt",
                ProductCode = "TSH-001",
                CategoryId = 501,
                BasePrice = 100m,
                CostPrice = 75m,
                Status = ProductStatus.Active,
                HasVariants = true
            },
            new Product
            {
                Id = 1002,
                Name = "Legacy Sneakers",
                ProductCode = "SNK-999",
                CategoryId = 501,
                BasePrice = 220m,
                CostPrice = 180m,
                Status = ProductStatus.Inactive,
                HasVariants = true
            });

        _db.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = 2001,
                ProductId = 1001,
                Name = "Red / Small",
                Sku = "TSH-RED-S",
                Barcode = "111111",
                Attributes = "{\"color\":\"Crimson\",\"size\":\"S\"}",
                PriceAdjustment = 5m
            },
            new ProductVariant
            {
                Id = 2002,
                ProductId = 1001,
                Name = "Blue / Medium",
                Sku = "TSH-BLU-M",
                Barcode = "222222",
                Attributes = "{\"color\":\"Blue\",\"size\":\"M\"}",
                PriceAdjustment = 7m
            },
            new ProductVariant
            {
                Id = 2003,
                ProductId = 1002,
                Name = "Default",
                Sku = "SNK-999-DEF",
                Barcode = "333333",
                Attributes = "{}",
                PriceAdjustment = 0m
            });

        _db.SaveChanges();
    }
}
