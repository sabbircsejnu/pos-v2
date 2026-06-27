using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Product;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Products;

public class ProductVariantSearchHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ProductVariantSearchHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SearchVariants_ByVariantSku_ReturnsMatchingVariant()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductClient();

        var response = await client.GetAsync("/api/products/variants/search?query=TSH-RED-S&pageNumber=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadApiData<List<ProductVariantSearchDto>>(response);
        Assert.NotNull(data);
        Assert.Contains(data!, v => v.Sku == "TSH-RED-S");
    }

    [Fact]
    public async Task SearchVariants_ByExactMainProductCode_ReturnsAllVariantsWithProductCode()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductClient();

        var response = await client.GetAsync("/api/products/variants/search?query=TSH-001&pageNumber=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadApiData<List<ProductVariantSearchDto>>(response);
        Assert.NotNull(data);
        Assert.Equal(2, data!.Count);
        Assert.All(data, v => Assert.Equal(9001, v.ProductId));
        Assert.All(data, v => Assert.Equal("TSH-001", v.ProductCode));
    }

    [Fact]
    public async Task SearchVariants_ByPartialMainProductCode_ReturnsAllVariantsUnderMatchedProduct()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductClient();

        var response = await client.GetAsync("/api/products/variants/search?query=TSH&pageNumber=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadApiData<List<ProductVariantSearchDto>>(response);
        Assert.NotNull(data);
        Assert.Equal(2, data!.Count);
        Assert.All(data, v => Assert.Equal(9001, v.ProductId));
    }

    [Fact]
    public async Task SearchVariants_ByProductName_ReturnsAllVariantsUnderThatProduct()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductClient();

        var response = await client.GetAsync("/api/products/variants/search?query=Classic%20T-Shirt&pageNumber=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadApiData<List<ProductVariantSearchDto>>(response);
        Assert.NotNull(data);
        Assert.Equal(2, data!.Count);
        Assert.All(data, v => Assert.Equal(9001, v.ProductId));
    }

    [Fact]
    public async Task SearchVariants_ByVariantAttributes_ReturnsMatchingVariant()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductClient();

        var response = await client.GetAsync("/api/products/variants/search?query=Crimson&pageNumber=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadApiData<List<ProductVariantSearchDto>>(response);
        Assert.NotNull(data);
        Assert.Single(data!);
        Assert.Equal("TSH-RED-S", data[0].Sku);
        Assert.Equal("TSH-001", data[0].ProductCode);
    }

    [Fact]
    public async Task SearchVariants_ByVariantBarcode_ReturnsMatchingVariant()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductClient();

        var response = await client.GetAsync("/api/products/variants/search?query=BRC-RED-S&pageNumber=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await ReadApiData<List<ProductVariantSearchDto>>(response);
        Assert.NotNull(data);
        Assert.Single(data!);
        Assert.Equal("TSH-RED-S", data[0].Sku);
    }

    private HttpClient CreateProductClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "777");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "products.view,products.view_cost");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Categories.Add(new Category
        {
            Id = 7001,
            Name = "Apparel",
            Description = "Search test category"
        });

        db.Products.AddRange(
            new Product
            {
                Id = 9001,
                Name = "Classic T-Shirt",
                ProductCode = "TSH-001",
                CategoryId = 7001,
                BasePrice = 100m,
                CostPrice = 70m,
                Status = ProductStatus.Active,
                HasVariants = true
            },
            new Product
            {
                Id = 9002,
                Name = "Inactive Product",
                ProductCode = "TSH-OLD",
                CategoryId = 7001,
                BasePrice = 80m,
                CostPrice = 55m,
                Status = ProductStatus.Inactive,
                HasVariants = true
            });

        db.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = 9901,
                ProductId = 9001,
                Name = "Red / Small",
                Sku = "TSH-RED-S",
                Barcode = "BRC-RED-S",
                Attributes = "{\"color\":\"Crimson\",\"size\":\"S\"}",
                PriceAdjustment = 5m
            },
            new ProductVariant
            {
                Id = 9902,
                ProductId = 9001,
                Name = "Blue / Medium",
                Sku = "TSH-BLU-M",
                Barcode = "BRC-BLU-M",
                Attributes = "{\"color\":\"Blue\",\"size\":\"M\"}",
                PriceAdjustment = 7m
            },
            new ProductVariant
            {
                Id = 9903,
                ProductId = 9002,
                Name = "Default",
                Sku = "TSH-OLD-DEF",
                Barcode = "BRC-OLD",
                Attributes = "{}",
                PriceAdjustment = 0m
            });

        await db.SaveChangesAsync();
    }

    private static async Task<T?> ReadApiData<T>(HttpResponseMessage response) where T : class
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, JsonOptions);
        return envelope?.Data;
    }

    private sealed class ApiEnvelope<T> where T : class
    {
        public T? Data { get; set; }
    }
}
