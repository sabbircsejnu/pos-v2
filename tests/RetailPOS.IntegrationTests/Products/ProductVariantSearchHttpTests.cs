using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    public async Task GetProductVariations_PreservesOptionOrderingAndSelectedOptionFallback()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Variations.AddRange(
                new Variation { Id = 7101, Name = "Size", DisplayOrder = 1, IsActive = true },
                new Variation { Id = 7102, Name = "Color", DisplayOrder = 2, IsActive = true });
            db.VariationOptions.AddRange(
                new VariationOption
                {
                    Id = 7201,
                    VariationId = 7101,
                    Name = "Small",
                    DisplayOrder = 2,
                    IsActive = true
                },
                new VariationOption
                {
                    Id = 7202,
                    VariationId = 7101,
                    Name = "Large",
                    PriceAdjustment = 5m,
                    DisplayOrder = 1,
                    IsActive = false
                },
                new VariationOption
                {
                    Id = 7211,
                    VariationId = 7102,
                    Name = "Blue",
                    DisplayOrder = 2,
                    IsActive = true
                },
                new VariationOption
                {
                    Id = 7212,
                    VariationId = 7102,
                    Name = "Red",
                    DisplayOrder = 1,
                    IsActive = true
                });
            db.ProductVariations.AddRange(
                new ProductVariation
                {
                    Id = 7301,
                    ProductId = 9001,
                    VariationId = 7101,
                    IsRequired = true
                },
                new ProductVariation
                {
                    Id = 7302,
                    ProductId = 9001,
                    VariationId = 7102,
                    IsRequired = false
                });
            db.ProductVariationSelectedOptions.Add(new ProductVariationSelectedOption
            {
                Id = 7401,
                ProductId = 9001,
                VariationId = 7101,
                OptionId = 7201
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateProductClient();
        using var response = await client.GetAsync("/api/products/9001/variations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var variations = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, variations.Length);
        Assert.Equal(7101, variations[0].GetProperty("variationId").GetInt64());
        Assert.Equal("Size", variations[0].GetProperty("variationName").GetString());
        Assert.True(variations[0].GetProperty("isRequired").GetBoolean());
        Assert.Equal(
            [7202L, 7201L],
            variations[0].GetProperty("options").EnumerateArray()
                .Select(option => option.GetProperty("id").GetInt64()));
        Assert.Equal([7201L], variations[0].GetProperty("selectedOptionIds").EnumerateArray()
            .Select(optionId => optionId.GetInt64()));
        Assert.Equal(
            [7212L, 7211L],
            variations[1].GetProperty("options").EnumerateArray()
                .Select(option => option.GetProperty("id").GetInt64()));
        Assert.Equal([7212L, 7211L], variations[1].GetProperty("selectedOptionIds").EnumerateArray()
            .Select(optionId => optionId.GetInt64()));

        using var missingResponse = await client.GetAsync("/api/products/9999/variations");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var missingDocument = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Product with ID 9999 not found",
            missingDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AssignVariations_PreservesReplacementDefaultsAndResponse()
    {
        await ResetAndSeedAsync();
        DateTime originalUpdatedAt;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var product = await db.Products.SingleAsync(candidate => candidate.Id == 9001);
            originalUpdatedAt = product.UpdatedAt;
            db.Variations.AddRange(
                new Variation { Id = 7301, Name = "Color", DisplayOrder = 1 },
                new Variation { Id = 7302, Name = "Size", DisplayOrder = 2 });
            db.VariationOptions.AddRange(
                new VariationOption { Id = 7401, VariationId = 7301, Name = "Blue", DisplayOrder = 1 },
                new VariationOption { Id = 7402, VariationId = 7301, Name = "Black", DisplayOrder = 2, IsActive = false },
                new VariationOption { Id = 7411, VariationId = 7302, Name = "Small", DisplayOrder = 1 },
                new VariationOption { Id = 7412, VariationId = 7302, Name = "Large", DisplayOrder = 2, IsActive = false });
            db.ProductVariations.Add(new ProductVariation
            {
                Id = 7501,
                ProductId = 9001,
                VariationId = 7301,
                IsRequired = true
            });
            db.ProductVariationSelectedOptions.Add(new ProductVariationSelectedOption
            {
                Id = 7601,
                ProductId = 9001,
                VariationId = 7301,
                OptionId = 7401
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateProductEditClient();
        using var response = await client.PostAsJsonAsync(
            "/api/products/9001/variations/assign",
            new
            {
                variationIds = new long[] { 7301, 7302 },
                selectedOptionsByVariation = new Dictionary<long, List<long>>
                {
                    [7301] = [7402]
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                "Variations assigned successfully",
                responseDocument.RootElement.GetProperty("message").GetString());
        }

        using var retryResponse = await client.PostAsJsonAsync(
            "/api/products/9001/variations/assign",
            new
            {
                variationIds = new long[] { 7301, 7302 },
                selectedOptionsByVariation = new Dictionary<long, List<long>>
                {
                    [7301] = [7402]
                }
            });
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var assignments = await db.ProductVariations
                .Where(assignment => assignment.ProductId == 9001)
                .OrderBy(assignment => assignment.VariationId)
                .ToListAsync();
            Assert.Equal(new long[] { 7301, 7302 }, assignments.Select(assignment => assignment.VariationId));
            Assert.True(assignments[0].IsRequired);
            Assert.False(assignments[1].IsRequired);

            var selectedOptions = await db.ProductVariationSelectedOptions
                .Where(selected => selected.ProductId == 9001)
                .Select(selected => new { selected.VariationId, selected.OptionId })
                .ToListAsync();
            Assert.Equal(
                new[] { (7301L, 7402L), (7302L, 7411L) },
                selectedOptions
                    .OrderBy(selected => selected.VariationId)
                    .Select(selected => (selected.VariationId, selected.OptionId)));

            var product = await db.Products.SingleAsync(candidate => candidate.Id == 9001);
            Assert.True(product.HasVariants);
            Assert.True(product.UpdatedAt >= originalUpdatedAt);
        }

        using var clearResponse = await client.PostAsJsonAsync(
            "/api/products/9001/variations/assign",
            new
            {
                variationIds = Array.Empty<long>(),
                selectedOptionsByVariation = new Dictionary<long, List<long>>()
            });
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        using (var clearDocument = JsonDocument.Parse(await clearResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                "Variations assigned successfully",
                clearDocument.RootElement.GetProperty("message").GetString());
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await verifyDb.ProductVariations.Where(row => row.ProductId == 9001).ToListAsync());
        Assert.Empty(await verifyDb.ProductVariationSelectedOptions.Where(row => row.ProductId == 9001).ToListAsync());
        Assert.False((await verifyDb.Products.SingleAsync(candidate => candidate.Id == 9001)).HasVariants);
    }

    [Fact]
    public async Task GenerateAllCombinations_UsesSelectedOptionsAndSkipsExistingCombinations()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Variations.AddRange(
                new Variation { Id = 7301, Name = "Size", DisplayOrder = 1 },
                new Variation { Id = 7302, Name = "Color", DisplayOrder = 2 });
            db.VariationOptions.AddRange(
                new VariationOption { Id = 7401, VariationId = 7301, Name = "Small", PriceAdjustment = -2m, DisplayOrder = 1 },
                new VariationOption { Id = 7402, VariationId = 7301, Name = "Large", PriceAdjustment = 4m, DisplayOrder = 2 },
                new VariationOption { Id = 7411, VariationId = 7302, Name = "Blue", PriceAdjustment = 3m, DisplayOrder = 1 },
                new VariationOption { Id = 7412, VariationId = 7302, Name = "Red", PriceAdjustment = 5m, DisplayOrder = 2 });
            db.ProductVariations.AddRange(
                new ProductVariation { ProductId = 9001, VariationId = 7301 },
                new ProductVariation { ProductId = 9001, VariationId = 7302 });
            db.ProductVariationSelectedOptions.AddRange(
                new ProductVariationSelectedOption { ProductId = 9001, VariationId = 7301, OptionId = 7401 },
                new ProductVariationSelectedOption { ProductId = 9001, VariationId = 7301, OptionId = 7402 },
                new ProductVariationSelectedOption { ProductId = 9001, VariationId = 7302, OptionId = 7411 });
            await db.SaveChangesAsync();
        }

        using var client = CreateProductEditClient();
        using var response = await client.PostAsync(
            "/api/products/9001/variations/combinations/generate-all",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var data = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
            Assert.Equal(2, data.Length);
            Assert.Equal(
                "Successfully generated 2 new combination(s)",
                document.RootElement.GetProperty("message").GetString());
            Assert.Equal("Size: Small / Color: Blue", data[0].GetProperty("combinationName").GetString());
            Assert.Equal(1m, data[0].GetProperty("priceAdjustment").GetDecimal());
            Assert.Equal(101m, data[0].GetProperty("finalPrice").GetDecimal());
            Assert.Equal("TSH-001-SMA-BLU", data[0].GetProperty("sku").GetString());
            Assert.Equal(2, data[0].GetProperty("optionIds").GetArrayLength());
            Assert.StartsWith("880001", data[0].GetProperty("barcode").GetString());
            Assert.Equal(13, data[0].GetProperty("barcode").GetString()!.Length);
        }

        using var retryResponse = await client.PostAsync(
            "/api/products/9001/variations/combinations/generate-all",
            content: null);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        using (var retryDocument = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync()))
        {
            Assert.Empty(retryDocument.RootElement.GetProperty("data").EnumerateArray());
            Assert.Equal(
                "All combinations already exist",
                retryDocument.RootElement.GetProperty("message").GetString());
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var generatedVariants = await verifyDb.ProductVariants
            .Where(variant => variant.ProductId == 9001 && variant.ProductVariantOptions.Any())
            .Include(variant => variant.ProductVariantOptions)
            .ToListAsync();
        Assert.Equal(2, generatedVariants.Count);
        Assert.All(generatedVariants, variant => Assert.Equal(2, variant.ProductVariantOptions.Count));
    }

    [Fact]
    public async Task GenerateSelectedCombinations_PreservesSubsetAndEmptyMeansAllAssigned()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Variations.AddRange(
                new Variation { Id = 7301, Name = "Size", DisplayOrder = 1 },
                new Variation { Id = 7302, Name = "Color", DisplayOrder = 2 });
            db.VariationOptions.AddRange(
                new VariationOption { Id = 7401, VariationId = 7301, Name = "Small", PriceAdjustment = -2m, DisplayOrder = 1 },
                new VariationOption { Id = 7402, VariationId = 7301, Name = "Large", PriceAdjustment = 4m, DisplayOrder = 2 },
                new VariationOption { Id = 7411, VariationId = 7302, Name = "Blue", PriceAdjustment = 3m, DisplayOrder = 1 },
                new VariationOption { Id = 7412, VariationId = 7302, Name = "Red", PriceAdjustment = 5m, DisplayOrder = 2 });
            db.ProductVariations.AddRange(
                new ProductVariation { ProductId = 9001, VariationId = 7301 },
                new ProductVariation { ProductId = 9001, VariationId = 7302 });
            db.ProductVariationSelectedOptions.AddRange(
                new ProductVariationSelectedOption { ProductId = 9001, VariationId = 7301, OptionId = 7401 },
                new ProductVariationSelectedOption { ProductId = 9001, VariationId = 7301, OptionId = 7402 },
                new ProductVariationSelectedOption { ProductId = 9001, VariationId = 7302, OptionId = 7411 },
                new ProductVariationSelectedOption { ProductId = 9001, VariationId = 7302, OptionId = 7412 });
            await db.SaveChangesAsync();
        }

        using var client = CreateProductEditClient();
        using var selectedResponse = await client.PostAsJsonAsync(
            "/api/products/9001/variations/combinations/generate-selected",
            new { variationIds = new long[] { 7302 } });

        Assert.Equal(HttpStatusCode.OK, selectedResponse.StatusCode);
        using (var selectedDocument = JsonDocument.Parse(await selectedResponse.Content.ReadAsStringAsync()))
        {
            var selected = selectedDocument.RootElement.GetProperty("data").EnumerateArray().ToArray();
            Assert.Equal(2, selected.Length);
            Assert.All(selected, combination =>
                Assert.All(combination.GetProperty("optionIds").EnumerateArray(),
                    optionId => Assert.Contains(optionId.GetInt64(), new long[] { 7411, 7412 })));
        }

        using var allResponse = await client.PostAsJsonAsync(
            "/api/products/9001/variations/combinations/generate-selected",
            new { variationIds = Array.Empty<long>() });

        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        using (var allDocument = JsonDocument.Parse(await allResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(4, allDocument.RootElement.GetProperty("data").GetArrayLength());
            Assert.Equal(
                "Successfully generated 4 new combination(s)",
                allDocument.RootElement.GetProperty("message").GetString());
        }

        using var invalidResponse = await client.PostAsJsonAsync(
            "/api/products/9001/variations/combinations/generate-selected",
            new { variationIds = new long[] { 9999 } });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        using (var invalidDocument = JsonDocument.Parse(await invalidResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                "Variation ID(s) 9999 are not assigned to product 9001. " +
                "Assigned variation IDs are: 7301, 7302.",
                invalidDocument.RootElement.GetProperty("message").GetString());
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(6, await verifyDb.ProductVariants
            .Where(variant => variant.ProductId == 9001 && variant.ProductVariantOptions.Any())
            .CountAsync());
    }

    [Fact]
    public async Task GetCombinations_PreservesVariantAndSelectedOptionProjection()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Variations.Add(new Variation { Id = 7301, Name = "Color", DisplayOrder = 1 });
            db.VariationOptions.AddRange(
                new VariationOption { Id = 7401, VariationId = 7301, Name = "Blue", PriceAdjustment = 3m, DisplayOrder = 1 },
                new VariationOption { Id = 7402, VariationId = 7301, Name = "Red", PriceAdjustment = 5m, DisplayOrder = 2 });
            db.ProductVariantOptions.AddRange(
                new ProductVariantOption { VariantId = 9901, OptionId = 7401 },
                new ProductVariantOption { VariantId = 9902, OptionId = 7402 });
            await db.SaveChangesAsync();
        }

        using var client = CreateProductClient();
        using var response = await client.GetAsync("/api/products/9001/variations/combinations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var combinations = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
            Assert.Equal(2, combinations.Length);
            Assert.Equal(9901, combinations[0].GetProperty("id").GetInt64());
            Assert.Equal("TSH-RED-S", combinations[0].GetProperty("sku").GetString());
            Assert.Equal(105m, combinations[0].GetProperty("finalPrice").GetDecimal());
            Assert.Equal(new long[] { 7401 }, combinations[0].GetProperty("optionIds")
                .EnumerateArray()
                .Select(optionId => optionId.GetInt64()));
            Assert.Equal("Color", combinations[0].GetProperty("options")[0]
                .GetProperty("variationName").GetString());
            Assert.Equal("Blue", combinations[0].GetProperty("options")[0]
                .GetProperty("optionName").GetString());
        }

        using var missingResponse = await client.GetAsync("/api/products/9999/variations/combinations");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var missingDocument = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Product with ID 9999 not found",
            missingDocument.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CreateManualCombination_PreservesPricingIdentifiersAndCreatedResponse()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Variations.AddRange(
                new Variation { Id = 7301, Name = "Color", DisplayOrder = 1 },
                new Variation { Id = 7302, Name = "Size", DisplayOrder = 2 });
            db.VariationOptions.AddRange(
                new VariationOption { Id = 7401, VariationId = 7301, Name = "Blue", PriceAdjustment = 3m },
                new VariationOption { Id = 7402, VariationId = 7302, Name = "Small", PriceAdjustment = -2m });
            await db.SaveChangesAsync();
        }

        using var client = CreateProductEditClient();
        using var response = await client.PostAsJsonAsync(
            "/api/products/9001/variations/combinations",
            new
            {
                sku = " manual sku ",
                barcode = "8801010000019",
                priceAdjustment = 10m,
                costAdjustment = 4m,
                optionIds = new long[] { 7402, 7401 }
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            "/api/products/9001/variations/combinations",
            response.Headers.Location?.AbsolutePath);
        using (var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            var data = document.RootElement.GetProperty("data");
            Assert.Equal("Combination created successfully", document.RootElement.GetProperty("message").GetString());
            Assert.Equal("MANUAL-SKU", data.GetProperty("sku").GetString());
            Assert.Equal("8801010000019", data.GetProperty("barcode").GetString());
            Assert.Equal("Blue - Small", data.GetProperty("combinationName").GetString());
            Assert.Equal(11m, data.GetProperty("priceAdjustment").GetDecimal());
            Assert.Equal(4m, data.GetProperty("costAdjustment").GetDecimal());
            Assert.Equal(111m, data.GetProperty("finalPrice").GetDecimal());
            Assert.Equal(new long[] { 7402, 7401 }, data.GetProperty("optionIds")
                .EnumerateArray()
                .Select(optionId => optionId.GetInt64()));
        }

        using var retryResponse = await client.PostAsJsonAsync(
            "/api/products/9001/variations/combinations",
            new
            {
                sku = " manual sku ",
                barcode = "8801010000019",
                priceAdjustment = 10m,
                costAdjustment = 4m,
                optionIds = new long[] { 7402, 7401 }
            });
        Assert.Equal(HttpStatusCode.BadRequest, retryResponse.StatusCode);
        using (var retryDocument = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("This combination already exists", retryDocument.RootElement.GetProperty("message").GetString());
        }
    }

    [Fact]
    public async Task UpdateCombination_PreservesOwnIdentifiersAndAdjustments()
    {
        await ResetAndSeedAsync();
        var originalUpdatedAt = DateTime.MinValue;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Variations.Add(new Variation { Id = 7301, Name = "Color", DisplayOrder = 1 });
            db.VariationOptions.Add(new VariationOption
            {
                Id = 7401,
                VariationId = 7301,
                Name = "Blue",
                DisplayOrder = 1
            });
            db.ProductVariantOptions.Add(new ProductVariantOption { VariantId = 9901, OptionId = 7401 });
            await db.SaveChangesAsync();
            var seededVariant = await db.ProductVariants.SingleAsync(candidate => candidate.Id == 9901);
            seededVariant.Barcode = "8801234567890";
            await db.SaveChangesAsync();
            originalUpdatedAt = (await db.ProductVariants.SingleAsync(variant => variant.Id == 9901)).UpdatedAt;
        }

        using var client = CreateProductEditClient();
        using var response = await client.PutAsJsonAsync(
            "/api/products/9001/variations/combinations/9901",
            new
            {
                sku = "TSH-RED-S",
                barcode = "8801234567890",
                priceAdjustment = 8m,
                costAdjustment = 3m
            });

        var updateBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, updateBody);
        using (var document = JsonDocument.Parse(updateBody))
        {
            Assert.Equal("Combination updated successfully", document.RootElement.GetProperty("message").GetString());
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var variant = await verifyDb.ProductVariants.SingleAsync(candidate => candidate.Id == 9901);
        Assert.Equal("TSH-RED-S", variant.Sku);
        Assert.Equal("8801234567893", variant.Barcode);
        Assert.Equal(8m, variant.PriceAdjustment);
        Assert.Equal(3m, variant.CostAdjustment);
        Assert.True(variant.UpdatedAt >= originalUpdatedAt);
    }

    [Fact]
    public async Task DeleteCombination_PreservesResponseAndRemovesVariantInventory()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Variations.Add(new Variation { Id = 7301, Name = "Color", DisplayOrder = 1 });
            db.VariationOptions.Add(new VariationOption { Id = 7401, VariationId = 7301, Name = "Blue" });
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                VariantId = 9901,
                LocationId = 7999,
                LocationType = "outlet",
                Quantity = 5
            });
            await db.SaveChangesAsync();
        }

        using var client = CreateProductEditClient();
        using var response = await client.DeleteAsync("/api/products/9001/variations/combinations/9901");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Combination deleted successfully", document.RootElement.GetProperty("message").GetString());
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Null(await verifyDb.ProductVariants.SingleOrDefaultAsync(variant => variant.Id == 9901));
        Assert.Empty(await verifyDb.Inventories.Where(inventory => inventory.VariantId == 9901).ToListAsync());
    }

    [Fact]
    public async Task GetById_PreservesProductProjectionEnvelopeAndCostRedaction()
    {
        await ResetAndSeedAsync();
        using var costClient = CreateProductClient();

        using var response = await costClient.GetAsync("/api/products/9001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("Product retrieved successfully", document.RootElement.GetProperty("message").GetString());
        Assert.Equal(9001, data.GetProperty("id").GetInt64());
        Assert.Equal("Classic T-Shirt", data.GetProperty("name").GetString());
        Assert.Equal("Apparel", data.GetProperty("categoryName").GetString());
        Assert.Equal(70m, data.GetProperty("costPrice").GetDecimal());
        Assert.Equal(2, data.GetProperty("variantCount").GetInt32());
        Assert.Equal(0, data.GetProperty("totalStock").GetInt32());
        var variants = data.GetProperty("variants").EnumerateArray().ToArray();
        Assert.Equal(2, variants.Length);
        Assert.Contains(variants, variant => variant.GetProperty("sku").GetString() == "TSH-RED-S");
        Assert.Contains(variants, variant => variant.GetProperty("finalPrice").GetDecimal() == 105m);

        using var noCostClient = CreateProductClient(includeCost: false);
        using var redactedResponse = await noCostClient.GetAsync("/api/products/9001");
        Assert.Equal(HttpStatusCode.OK, redactedResponse.StatusCode);
        using var redactedDocument = JsonDocument.Parse(await redactedResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            JsonValueKind.Null,
            redactedDocument.RootElement.GetProperty("data").GetProperty("costPrice").ValueKind);

        using var missingResponse = await costClient.GetAsync("/api/products/9999");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var missingDocument = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Product with ID 9999 not found",
            missingDocument.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetAll_PreservesNameOrderInactiveProductsAndCostRedaction()
    {
        await ResetAndSeedAsync();
        using var costClient = CreateProductClient();

        using var response = await costClient.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Products retrieved successfully",
            document.RootElement.GetProperty("message").GetString());
        var products = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, products.Length);
        Assert.Equal(
            ["Classic T-Shirt", "Inactive Product"],
            products.Select(product => product.GetProperty("name").GetString()));
        Assert.Equal(70m, products[0].GetProperty("costPrice").GetDecimal());
        Assert.Equal("inactive", products[1].GetProperty("status").GetString());

        using var noCostClient = CreateProductClient(includeCost: false);
        using var redactedResponse = await noCostClient.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, redactedResponse.StatusCode);
        using var redactedDocument = JsonDocument.Parse(await redactedResponse.Content.ReadAsStringAsync());
        Assert.All(
            redactedDocument.RootElement.GetProperty("data").EnumerateArray(),
            product => Assert.Equal(
                JsonValueKind.Null,
                product.GetProperty("costPrice").ValueKind));
    }

    [Fact]
    public async Task GetBySkuAndBarcode_PreserveProductDetailsAndNotFoundContracts()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var product = (await db.Products
                .Where(candidate => candidate.Id == 9001)
                .ToListAsync()).Single();
            product.Sku = "TSH-MAIN-001";
            product.Barcode = "BRC-MAIN-001";
            await db.SaveChangesAsync();
        }

        using var client = CreateProductClient();
        using var detailResponse = await client.GetAsync("/api/products/9001");
        using var detailDocument = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        using var skuResponse = await client.GetAsync("/api/products/sku/TSH-MAIN-001");
        Assert.Equal(HttpStatusCode.OK, skuResponse.StatusCode);
        using var skuDocument = JsonDocument.Parse(await skuResponse.Content.ReadAsStringAsync());
        var skuData = skuDocument.RootElement.GetProperty("data");
        // Only the by-id read enriches stock for the caller's locations; SKU and barcode reads report it as unavailable.
        Assert.Equal("not_initialized", detailDocument.RootElement.GetProperty("data").GetProperty("stockStatus").GetString());
        Assert.Equal("unavailable", skuData.GetProperty("stockStatus").GetString());
        Assert.Equal(
            WithoutStock(detailDocument.RootElement.GetProperty("data")),
            WithoutStock(skuData));
        Assert.Equal("Apparel", skuData.GetProperty("categoryName").GetString());
        Assert.Equal(70m, skuData.GetProperty("costPrice").GetDecimal());
        Assert.Equal(2, skuData.GetProperty("variants").GetArrayLength());
        Assert.Equal("Product retrieved successfully", skuDocument.RootElement.GetProperty("message").GetString());

        using var barcodeResponse = await client.GetAsync("/api/products/barcode/BRC-MAIN-001");
        Assert.Equal(HttpStatusCode.OK, barcodeResponse.StatusCode);
        using var barcodeDocument = JsonDocument.Parse(await barcodeResponse.Content.ReadAsStringAsync());
        Assert.Equal(WithoutStock(skuData), WithoutStock(barcodeDocument.RootElement.GetProperty("data")));
        Assert.Equal("Product retrieved successfully", barcodeDocument.RootElement.GetProperty("message").GetString());

        using var noCostClient = CreateProductClient(includeCost: false);
        using var redactedSkuResponse = await noCostClient.GetAsync("/api/products/sku/TSH-MAIN-001");
        using var redactedSkuDocument = JsonDocument.Parse(await redactedSkuResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            JsonValueKind.Null,
            redactedSkuDocument.RootElement.GetProperty("data").GetProperty("costPrice").ValueKind);
        using var redactedBarcodeResponse = await noCostClient.GetAsync("/api/products/barcode/BRC-MAIN-001");
        using var redactedBarcodeDocument = JsonDocument.Parse(await redactedBarcodeResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            JsonValueKind.Null,
            redactedBarcodeDocument.RootElement.GetProperty("data").GetProperty("costPrice").ValueKind);

        using var missingSkuResponse = await client.GetAsync("/api/products/sku/UNKNOWN");
        Assert.Equal(HttpStatusCode.NotFound, missingSkuResponse.StatusCode);
        using var missingSkuDocument = JsonDocument.Parse(await missingSkuResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Product with SKU 'UNKNOWN' not found",
            missingSkuDocument.RootElement.GetProperty("error").GetString());

        using var missingBarcodeResponse = await client.GetAsync("/api/products/barcode/UNKNOWN");
        Assert.Equal(HttpStatusCode.NotFound, missingBarcodeResponse.StatusCode);
        using var missingBarcodeDocument = JsonDocument.Parse(await missingBarcodeResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Product with barcode 'UNKNOWN' not found",
            missingBarcodeDocument.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetByCategory_PreservesNameOrderInactiveProductsEnvelopeAndCostRedaction()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductClient();

        using var response = await client.GetAsync("/api/products/category/7001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var products = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(
            "Products retrieved successfully",
            document.RootElement.GetProperty("message").GetString());
        Assert.Equal(
            ["Classic T-Shirt", "Inactive Product"],
            products.Select(product => product.GetProperty("name").GetString()));
        Assert.Equal(70m, products[0].GetProperty("costPrice").GetDecimal());
        Assert.Equal("inactive", products[1].GetProperty("status").GetString());

        using var noCostClient = CreateProductClient(includeCost: false);
        using var redactedResponse = await noCostClient.GetAsync("/api/products/category/7001");
        using var redactedDocument = JsonDocument.Parse(await redactedResponse.Content.ReadAsStringAsync());
        Assert.All(
            redactedDocument.RootElement.GetProperty("data").EnumerateArray(),
            product => Assert.Equal(
                JsonValueKind.Null,
                product.GetProperty("costPrice").ValueKind));

        using var missingResponse = await client.GetAsync("/api/products/category/7999");
        Assert.Equal(HttpStatusCode.OK, missingResponse.StatusCode);
        using var missingDocument = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync());
        Assert.Empty(missingDocument.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Search_PreservesFilteringSortingPaginationEnvelopeAndCostRedaction()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductClient();
        var search = new
        {
            searchQuery = "TSH",
            categoryId = 7001,
            hasVariants = true,
            minPrice = 80m,
            maxPrice = 100m,
            pageNumber = 1,
            pageSize = 1,
            sortBy = "price",
            sortOrder = "desc"
        };

        using var firstPageResponse = await client.PostAsJsonAsync("/api/products/search", search);
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        using var firstPageDocument = JsonDocument.Parse(await firstPageResponse.Content.ReadAsStringAsync());
        var firstPage = firstPageDocument.RootElement.GetProperty("data");
        Assert.Equal("Products searched successfully", firstPageDocument.RootElement.GetProperty("message").GetString());
        Assert.Equal(2, firstPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, firstPage.GetProperty("pageNumber").GetInt32());
        Assert.Equal(1, firstPage.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, firstPage.GetProperty("totalPages").GetInt32());
        Assert.True(firstPage.GetProperty("hasNextPage").GetBoolean());
        Assert.False(firstPage.GetProperty("hasPreviousPage").GetBoolean());
        var firstProduct = Assert.Single(firstPage.GetProperty("products").EnumerateArray());
        Assert.Equal(9001, firstProduct.GetProperty("id").GetInt64());
        Assert.Equal(70m, firstProduct.GetProperty("costPrice").GetDecimal());

        search = new
        {
            searchQuery = "TSH",
            categoryId = 7001,
            hasVariants = true,
            minPrice = 80m,
            maxPrice = 100m,
            pageNumber = 2,
            pageSize = 1,
            sortBy = "price",
            sortOrder = "desc"
        };
        using var secondPageResponse = await client.PostAsJsonAsync("/api/products/search", search);
        using var secondPageDocument = JsonDocument.Parse(await secondPageResponse.Content.ReadAsStringAsync());
        var secondPage = secondPageDocument.RootElement.GetProperty("data");
        Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);
        Assert.Equal(9002, Assert.Single(secondPage.GetProperty("products").EnumerateArray())
            .GetProperty("id").GetInt64());
        Assert.False(secondPage.GetProperty("hasNextPage").GetBoolean());
        Assert.True(secondPage.GetProperty("hasPreviousPage").GetBoolean());

        using var noCostClient = CreateProductClient(includeCost: false);
        using var redactedResponse = await noCostClient.PostAsJsonAsync("/api/products/search", search);
        using var redactedDocument = JsonDocument.Parse(await redactedResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            JsonValueKind.Null,
            Assert.Single(redactedDocument.RootElement.GetProperty("data").GetProperty("products").EnumerateArray())
                .GetProperty("costPrice").ValueKind);
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

    [Fact]
    public async Task SearchVariants_PreservesLocationStockAndCostRedaction()
    {
        await ResetAndSeedAsync();
        await GrantOutletAccessAsync(7999);
        using var client = CreateProductClient();

        var response = await client.GetAsync(
            "/api/products/variants/search?query=TSH-RED-S&pageNumber=1&pageSize=20&locationId=7999&locationType=outlet");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadApiData<List<ProductVariantSearchDto>>(response);
        var variant = Assert.Single(data!);
        Assert.Equal(0, variant.StockQuantity);
        Assert.Equal(70m, variant.CostPrice);
        Assert.Equal("Color Crimson / Size S", variant.VariantDisplay);

        using var noCostClient = CreateProductClient(includeCost: false);
        var redactedResponse = await noCostClient.GetAsync(
            "/api/products/variants/search?query=TSH-RED-S&pageNumber=1&pageSize=20&locationId=7999&locationType=outlet");
        var redactedData = await ReadApiData<List<ProductVariantSearchDto>>(redactedResponse);
        Assert.Null(Assert.Single(redactedData!).CostPrice);
    }

    [Fact]
    public async Task BarcodeVariantSearch_PreservesSearchResponseAndVariantDisplay()
    {
        await ResetAndSeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "777");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "barcode.view");

        var response = await client.GetAsync(
            "/api/barcodes/variants/search?query=TSH-RED-S&pageNumber=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadApiData<List<ProductVariantSearchDto>>(response);
        var variant = Assert.Single(data!);
        Assert.Equal("TSH-RED-S", variant.Sku);
        Assert.Equal("Color Crimson / Size S", variant.VariantDisplay);
        Assert.Equal(105m, variant.FinalPrice);
    }

    [Fact]
    public async Task CreateProduct_PreservesNormalizationVariantIdentifiersAndResponse()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductCreateClient();

        using var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "New Product",
            description = "Created by product characterization",
            productCode = " new-001 ",
            sku = "MAIN-SKU",
            barcode = "MAIN-BARCODE",
            categoryId = 7001,
            basePrice = 25m,
            costPrice = 11m,
            taxRate = 7.5m,
            status = "draft",
            hasVariants = true,
            variants = new[]
            {
                new
                {
                    name = "Blue-Small",
                    attributes = "{\"color\":\"Blue\",\"size\":\"Small\"}",
                    priceAdjustment = 2.5m
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var product = document.RootElement.GetProperty("data");
        Assert.Equal("New Product", product.GetProperty("name").GetString());
        Assert.Equal("NEW-001", product.GetProperty("productCode").GetString());
        Assert.Equal("MAIN-SKU", product.GetProperty("sku").GetString());
        Assert.Equal("MAIN-BARCODE", product.GetProperty("barcode").GetString());
        Assert.Equal("Apparel", product.GetProperty("categoryName").GetString());
        Assert.Equal(25m, product.GetProperty("basePrice").GetDecimal());
        Assert.Equal(11m, product.GetProperty("costPrice").GetDecimal());
        Assert.Equal(7.5m, product.GetProperty("taxRate").GetDecimal());
        Assert.Equal("draft", product.GetProperty("status").GetString());
        Assert.False(product.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, product.GetProperty("variantCount").GetInt32());
        var variant = Assert.Single(product.GetProperty("variants").EnumerateArray());
        Assert.Equal("Blue-Small", variant.GetProperty("name").GetString());
        Assert.Equal("{\"color\":\"Blue\",\"size\":\"Small\"}", variant.GetProperty("attributes").GetString());
        Assert.Equal(2.5m, variant.GetProperty("priceAdjustment").GetDecimal());
        Assert.Equal(27.5m, variant.GetProperty("finalPrice").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(variant.GetProperty("sku").GetString()));
        Assert.Equal(13, variant.GetProperty("barcode").GetString()!.Length);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var saved = await db.Products.SingleAsync(item => item.Name == "New Product");
        Assert.Equal("NEW-001", saved.ProductCode);
        Assert.Equal(ProductStatus.Draft, saved.Status);
        Assert.Equal(saved.Id, await db.ProductVariants
            .Where(item => item.ProductId == saved.Id)
            .Select(item => item.ProductId)
            .SingleAsync());
    }

    [Fact]
    public async Task CreateProduct_MissingProductCodeForVariantsReturnsBadRequestWithoutPersistingProduct()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductCreateClient();

        using var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "Missing Variant Product Code",
            categoryId = 7001,
            basePrice = 25m,
            hasVariants = true,
            variants = new[] { new { name = "Blue-Small" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Main Product Code is required when creating product variants",
            document.RootElement.GetProperty("error").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.False(await db.Products.AnyAsync(item => item.Name == "Missing Variant Product Code"));
    }

    [Fact]
    public async Task UpdateProduct_PreservesVariantFieldsAndAppliesExistingNormalizationDefaults()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var product = await db.Products.SingleAsync(item => item.Id == 9001);
            product.Barcode = "PRODUCT-BARCODE";
            await db.SaveChangesAsync();
        }

        using var client = CreateProductUpdateClient();
        using var response = await client.PutAsJsonAsync("/api/products/9001", new
        {
            name = "Updated T-Shirt",
            description = "Updated by legacy-path characterization",
            productCode = " tsh-002 ",
            categoryId = 7001,
            basePrice = 125m,
            taxRate = 8m,
            status = "inactive"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var productResponse = document.RootElement.GetProperty("data");
        Assert.Equal("Updated T-Shirt", productResponse.GetProperty("name").GetString());
        Assert.Equal("Updated by legacy-path characterization", productResponse.GetProperty("description").GetString());
        Assert.Equal("TSH-002", productResponse.GetProperty("productCode").GetString());
        Assert.Null(productResponse.GetProperty("sku").GetString());
        Assert.Null(productResponse.GetProperty("barcode").GetString());
        Assert.Equal(125m, productResponse.GetProperty("basePrice").GetDecimal());
        Assert.Equal(0m, productResponse.GetProperty("costPrice").GetDecimal());
        Assert.Equal(8m, productResponse.GetProperty("taxRate").GetDecimal());
        Assert.Equal("inactive", productResponse.GetProperty("status").GetString());
        Assert.False(productResponse.GetProperty("isActive").GetBoolean());

        Assert.Equal(2, productResponse.GetProperty("variants").GetArrayLength());
        var variant = productResponse.GetProperty("variants").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "Red / Small");
        Assert.Equal("TSH-002", variant.GetProperty("productCode").GetString());
        Assert.Equal("Red / Small", variant.GetProperty("name").GetString());
        Assert.Equal(130m, variant.GetProperty("finalPrice").GetDecimal());

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var saved = await verifyDb.Products.SingleAsync(item => item.Id == 9001);
        Assert.Equal("TSH-002", saved.ProductCode);
        Assert.Equal(
            "TSH-RED-S",
            (await verifyDb.ProductVariants.SingleAsync(item => item.Id == 9901)).Sku);
    }

    [Fact]
    public async Task UpdateProduct_RejectsDuplicateNameAndMissingProduct()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductUpdateClient();

        using var duplicateName = await client.PutAsJsonAsync("/api/products/9001", new
        {
            name = "Inactive Product",
            productCode = "TSH-001",
            categoryId = 7001,
            basePrice = 100m,
            status = "active"
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateName.StatusCode);
        using var duplicateDocument = JsonDocument.Parse(await duplicateName.Content.ReadAsStringAsync());
        Assert.Equal(
            "Product with name 'Inactive Product' already exists",
            duplicateDocument.RootElement.GetProperty("error").GetString());

        using var missing = await client.PutAsJsonAsync("/api/products/9999", new
        {
            name = "Missing Product",
            categoryId = 7001,
            basePrice = 10m
        });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var missingDocument = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Equal("Product with ID 9999 not found", missingDocument.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task UpdateProduct_RejectsDuplicateSkuBarcodeAndProductCode()
    {
        await ResetAndSeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var otherProduct = await db.Products.SingleAsync(item => item.Id == 9002);
            otherProduct.Sku = "EXISTING-SKU";
            otherProduct.Barcode = "EXISTING-BARCODE";
            otherProduct.ProductCode = "EXISTING-CODE";
            await db.SaveChangesAsync();
        }

        using var client = CreateProductUpdateClient();
        var updates = new[]
        {
            (Field: "sku", Value: "EXISTING-SKU", Message: "SKU 'EXISTING-SKU' already exists"),
            (Field: "barcode", Value: "EXISTING-BARCODE", Message: "Barcode 'EXISTING-BARCODE' already exists"),
            (Field: "productCode", Value: "EXISTING-CODE", Message: "Product code 'EXISTING-CODE' already exists")
        };

        foreach (var update in updates)
        {
            using var response = await client.PutAsJsonAsync(
                "/api/products/9001",
                new Dictionary<string, object>
                {
                    ["name"] = "Updated T-Shirt",
                    ["categoryId"] = 7001,
                    ["basePrice"] = 100m,
                    [update.Field] = update.Value
                });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(update.Message, document.RootElement.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task DeleteProduct_PreservesSuccessNotFoundAndVariantCascadeBehavior()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductDeleteClient();

        using var deleted = await client.DeleteAsync("/api/products/9001");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var deletedDocument = JsonDocument.Parse(await deleted.Content.ReadAsStringAsync());
        Assert.Equal(
            "Product deleted successfully",
            deletedDocument.RootElement.GetProperty("message").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.False(await db.Products.AnyAsync(item => item.Id == 9001));
            Assert.False(await db.ProductVariants.AnyAsync(item => item.ProductId == 9001));
        }

        using var missing = await client.DeleteAsync("/api/products/9999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var missingDocument = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Equal("Product with ID 9999 not found", missingDocument.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GenerateSku_PreservesNormalizedPrefixTimestampAndEnvelope()
    {
        await ResetAndSeedAsync();
        using var client = CreateProductClient();

        using var response = await client.PostAsJsonAsync("/api/products/generate-sku", "blue mouse");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sku = document.RootElement.GetProperty("data").GetString();
        Assert.NotNull(sku);
        Assert.Matches("^BLU-[0-9]{14}(-[0-9]+)?$", sku);
        Assert.Equal("SKU generated successfully", document.RootElement.GetProperty("message").GetString());
    }

    private async Task<(JsonElement Data, HttpStatusCode Status)> PostProductAsync(object body)
    {
        using var client = CreateProductCreateClient();
        using var response = await client.PostAsJsonAsync("/api/products", body);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (document.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default, response.StatusCode);
    }

    private async Task<(JsonElement Data, HttpStatusCode Status)> PutProductAsync(long id, object body)
    {
        using var client = CreateProductUpdateClient();
        using var response = await client.PutAsJsonAsync($"/api/products/{id}", body);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (document.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default, response.StatusCode);
    }

    [Fact]
    public async Task ProductTax_CreateKeepsNullZeroAndClassificationDistinctAndReturnsThemUnchanged()
    {
        await ResetAndSeedAsync();

        var (missing, missingStatus) = await PostProductAsync(
            new { name = "Tax missing", categoryId = 7001, basePrice = 10m });
        var (zero, zeroStatus) = await PostProductAsync(
            new { name = "Tax zero", categoryId = 7001, basePrice = 10m, taxRate = 0m });
        var (exempt, exemptStatus) = await PostProductAsync(
            new { name = "Tax exempt", categoryId = 7001, basePrice = 10m, taxRate = 5m, taxClassification = " EXEMPT " });

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.Created], [missingStatus, zeroStatus, exemptStatus]);
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("taxRate").ValueKind);
        Assert.Equal(0m, zero.GetProperty("taxRate").GetDecimal());
        Assert.Equal(5m, exempt.GetProperty("taxRate").GetDecimal());
        Assert.Equal("exempt", exempt.GetProperty("taxClassification").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Null((await db.Products.SingleAsync(item => item.Name == "Tax missing")).TaxRate);
        Assert.Equal(0m, (await db.Products.SingleAsync(item => item.Name == "Tax zero")).TaxRate);
        Assert.Null((await db.Products.SingleAsync(item => item.Name == "Tax zero")).TaxClassification);
    }

    [Fact]
    public async Task ProductTax_UpdateRoundTripsZeroAndNullWithoutCollapsingOneIntoTheOther()
    {
        await ResetAndSeedAsync();
        object Body(decimal? rate, string? classification = null) => new
        {
            name = "Classic T-Shirt", productCode = "TSH-001", categoryId = 7001, basePrice = 100m,
            status = "active", taxRate = rate, taxClassification = classification
        };

        var (toZero, _) = await PutProductAsync(9001, Body(0m));
        Assert.Equal(0m, toZero.GetProperty("taxRate").GetDecimal());

        // Saving the unchanged product again keeps the explicit 0%.
        var (stillZero, _) = await PutProductAsync(9001, Body(0m));
        Assert.Equal(0m, stillZero.GetProperty("taxRate").GetDecimal());

        var (toNull, _) = await PutProductAsync(9001, Body(null));
        Assert.Equal(JsonValueKind.Null, toNull.GetProperty("taxRate").ValueKind);

        var (zeroRated, _) = await PutProductAsync(9001, Body(null, "zero_rated"));
        Assert.Equal(JsonValueKind.Null, zeroRated.GetProperty("taxRate").ValueKind);
        Assert.Equal("zero_rated", zeroRated.GetProperty("taxClassification").GetString());

        var (backToStandard, _) = await PutProductAsync(9001, Body(8.5m, ""));
        Assert.Equal(8.5m, backToStandard.GetProperty("taxRate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, backToStandard.GetProperty("taxClassification").ValueKind);

        using var client = CreateProductClient();
        using var read = await client.GetAsync("/api/products/9001");
        using var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(8.5m, document.RootElement.GetProperty("data").GetProperty("taxRate").GetDecimal());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public async Task ProductTax_RejectsOutOfRangeRates(double rate)
    {
        await ResetAndSeedAsync();

        var (_, createStatus) = await PostProductAsync(
            new { name = "Bad rate", categoryId = 7001, basePrice = 10m, taxRate = (decimal)rate });
        var (_, updateStatus) = await PutProductAsync(
            9001, new { name = "Classic T-Shirt", productCode = "TSH-001", categoryId = 7001, basePrice = 100m, taxRate = (decimal)rate });

        Assert.Equal(HttpStatusCode.BadRequest, createStatus);
        Assert.Equal(HttpStatusCode.BadRequest, updateStatus);
    }

    [Fact]
    public async Task ProductTax_RejectsAnUnknownClassification()
    {
        await ResetAndSeedAsync();

        var (_, status) = await PostProductAsync(
            new { name = "Bad class", categoryId = 7001, basePrice = 10m, taxClassification = "reduced" });

        Assert.NotEqual(HttpStatusCode.Created, status);
    }

    private static string WithoutStock(JsonElement product)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(product.GetRawText())!.AsObject();
        node.Remove("stockStatus");
        node.Remove("totalStock");
        return node.ToJsonString();
    }

    private HttpClient CreateProductClient(bool includeCost = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "777");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add(
            "X-Test-Permissions",
            includeCost ? "products.view,products.view_cost" : "products.view");
        return client;
    }

    private HttpClient CreateProductCreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "777");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add(
            "X-Test-Permissions",
            "products.view,products.create,products.view_cost");
        return client;
    }

    private HttpClient CreateProductUpdateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "777");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add(
            "X-Test-Permissions",
            "products.view,products.edit,products.view_cost");
        return client;
    }

    private HttpClient CreateProductDeleteClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "777");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "products.view,products.delete");
        return client;
    }

    private HttpClient CreateProductEditClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "777");
        client.DefaultRequestHeaders.Add("X-Test-Role", "InventoryAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "products.view,products.edit");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.Add(new Business { Id = 1, Name = "Variant search test business", IsActive = true });
        db.Users.Add(new User
        {
            Id = 777,
            BusinessId = 1,
            Name = "Variant search user",
            Email = "variant-search@example.com",
            PasswordHash = "x",
            InventoryLocationAccessScope = User.InventoryAccessSpecific,
            IsActive = true
        });
        await db.SaveChangesAsync();

        db.Categories.Add(new Category
        {
            Id = 7001,
            BusinessId = 1,
            Name = "Apparel",
            Description = "Search test category"
        });

        db.Products.AddRange(
            new Product
            {
                Id = 9001,
                BusinessId = 1,
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
                BusinessId = 1,
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
                BusinessId = 1,
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
                BusinessId = 1,
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
                BusinessId = 1,
                ProductId = 9002,
                Name = "Default",
                Sku = "TSH-OLD-DEF",
                Barcode = "BRC-OLD",
                Attributes = "{}",
                PriceAdjustment = 0m
            });

        await db.SaveChangesAsync();
    }

    private async Task GrantOutletAccessAsync(long outletId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Outlets.Add(new Outlet
        {
            Id = outletId,
            BusinessId = 1,
            Name = "Variant search test outlet"
        });
        await db.SaveChangesAsync();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == 777);
        user.OutletId = outletId;
        db.UserOutletAssignments.Add(new UserOutletAssignment
        {
            Id = 1,
            UserId = 777,
            OutletId = outletId,
            BusinessId = 1,
            IsPrimary = true
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
