using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using RetailPOS.Application.Products.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Products;

namespace RetailPOS.Tests.Products;

public class ProductIdentifierServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly RetailPOSDbContext _db;
    private readonly ProductIdentifierResolver _service;

    public ProductIdentifierServiceTests()
    {
        _connection.Open();
        var opts = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new RetailPOSDbContext(opts);
        _db.Database.EnsureCreated();
        _db.Businesses.Add(new Business { Id = 1, Name = "Test Business" });
        _db.SaveChanges();
        _service = new ProductIdentifierResolver(new ProductIdentifierLookupStore(_db, new TestBusinessScope(1)));

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

        Assert.Equal("ABA001P-56-BLK-2", sku);
    }

    [Fact]
    public async Task ResolveVariantSkuAsync_NormalizesRequestedSku()
    {
        var sku = await _service.ResolveVariantSkuAsync(
            requestedSku: "  a bc  -- xyz ",
            mainProductCode: "ABA001P",
            sizeCode: "56",
            attributeCode: "BLK");

        Assert.Equal("A-BC-XYZ", sku);
    }

    [Fact]
    public async Task ResolveVariantIdentifiersAsync_AllowsCurrentVariantWhenExcluded()
    {
        var sku = await _service.ResolveVariantSkuAsync(
            requestedSku: "ABA001P-56-BLK",
            mainProductCode: "ABA001P",
            sizeCode: "56",
            attributeCode: "BLK",
            excludeVariantId: 601);
        var barcode = await _service.ResolveVariantBarcodeAsync(
            requestedBarcode: "8801010000019",
            categoryId: 101,
            excludeVariantId: 601);

        Assert.Equal("ABA001P-56-BLK", sku);
        Assert.Equal("8801010000019", barcode);
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

        Assert.Equal("8801010000026", barcode);
    }

    [Fact]
    public async Task ResolveVariantBarcodeAsync_Normalizes12DigitInputTo13Digits()
    {
        var normalized = await _service.ResolveVariantBarcodeAsync(
            requestedBarcode: "880101000002",
            categoryId: 101);

        Assert.Equal("8801010000026", normalized);
    }

    [Fact]
    public async Task ResolveVariantBarcodeAsync_RecalculatesProvidedEan13CheckDigit()
    {
        var normalized = await _service.ResolveVariantBarcodeAsync(
            requestedBarcode: "8801010000029",
            categoryId: 101);

        Assert.Equal("8801010000026", normalized);
    }

    [Fact]
    public async Task ResolveVariantBarcodeAsync_ThrowsOnDuplicateProvidedBarcode()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ResolveVariantBarcodeAsync(
                requestedBarcode: "8801010000019",
                categoryId: 101));
    }

    [Fact]
    public void ExtractCodesFromVariantText_PreservesJsonAndNameFallbackRules()
    {
        var fromJson = _service.ExtractCodesFromVariantText(
            "Color - Medium",
            "{\"size\":\"Extra Large\",\"color\":\"Blue\"}");
        var fromName = _service.ExtractCodesFromVariantText("Color - Medium", "not json");

        Assert.Equal(("EXT", "BLU"), fromJson);
        Assert.Equal(("MED", "COL"), fromName);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void SeedData()
    {
        _db.Categories.Add(new Category { Id = 101, BusinessId = 1, Name = "Apparel", Description = "" });

        _db.Products.Add(new Product
        {
            Id = 501,
            BusinessId = 1,
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
            BusinessId = 1,
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
