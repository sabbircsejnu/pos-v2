using Microsoft.EntityFrameworkCore;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.Tests.Products;

public class ProductIdentifierServiceTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly ProductIdentifierService _service;

    public ProductIdentifierServiceTests()
    {
        var opts = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(databaseName: $"id-service-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new RetailPOSDbContext(opts);
        var variantRepo = new ProductVariantRepository(_db);
        _service = new ProductIdentifierService(variantRepo, _db);

        SeedData();
    }

    [Fact]
    public async Task ResolveVariantSkuAsync_GeneratesExpectedPattern_WhenMissing()
    {
        var sku = await _service.ResolveVariantSkuAsync(
            requestedSku: null,
            mainProductCode: "ABA001P",
            sizeCode: "56",
            attributeCode: "BLK");

        Assert.StartsWith("ABA001P-56-BLK", sku);
    }

    [Fact]
    public async Task ResolveVariantSkuAsync_ThrowsOnDuplicateProvidedSku()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ResolveVariantSkuAsync(
                requestedSku: "ABA001P-56-BLK",
                mainProductCode: "ABA001P",
                sizeCode: "56",
                attributeCode: "BLK"));
    }

    [Fact]
    public async Task ResolveVariantBarcodeAsync_GeneratesNumericEan13_WhenMissing()
    {
        var barcode = await _service.ResolveVariantBarcodeAsync(requestedBarcode: null, categoryId: 101);

        Assert.Equal(13, barcode.Length);
        Assert.True(barcode.All(char.IsDigit));
    }

    [Fact]
    public async Task ResolveVariantBarcodeAsync_Normalizes12DigitInputTo13Digits()
    {
        var normalized = await _service.ResolveVariantBarcodeAsync(
            requestedBarcode: "880101000002",
            categoryId: 101);

        Assert.Equal(13, normalized.Length);
        Assert.True(normalized.All(char.IsDigit));
    }

    [Fact]
    public async Task ResolveVariantBarcodeAsync_ThrowsOnDuplicateProvidedBarcode()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ResolveVariantBarcodeAsync(
                requestedBarcode: "8801010000019",
                categoryId: 101));
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedData()
    {
        _db.Categories.Add(new Category { Id = 101, Name = "Apparel", Description = "" });

        _db.Products.Add(new Product
        {
            Id = 501,
            Name = "Test Shirt",
            ProductCode = "ABA001P",
            CategoryId = 101,
            BasePrice = 100m,
            CostPrice = 80m,
            Status = ProductStatus.Active,
            HasVariants = true
        });

        _db.ProductVariants.Add(new ProductVariant
        {
            Id = 601,
            ProductId = 501,
            Name = "Black / 56",
            Sku = "ABA001P-56-BLK",
            Barcode = "8801010000019",
            Attributes = "{\"size\":\"56\",\"color\":\"Black\"}",
            PriceAdjustment = 0m
        });

        _db.SaveChanges();
    }
}
