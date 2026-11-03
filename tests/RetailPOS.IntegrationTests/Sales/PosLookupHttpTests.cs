using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RetailPOS.API.DTOs.Pricing;
using RetailPOS.API.Services;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.Tests.Sales;

public sealed class PosLookupHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public PosLookupHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Lookup_ResolvesBarcodeSkuAndVariantWithOutletPriceAndStockProjection()
    {
        await ResetAndSeedAsync();

        using var client = CreatePosClient();

        using var barcodeResponse = await client.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=5001");
        Assert.Equal(HttpStatusCode.OK, barcodeResponse.StatusCode);
        using var barcodeDocument = JsonDocument.Parse(await barcodeResponse.Content.ReadAsStringAsync());
        var barcodeProduct = barcodeDocument.RootElement.GetProperty("data");
        Assert.Equal(9801, barcodeProduct.GetProperty("variantId").GetInt64());
        Assert.Equal(9101, barcodeProduct.GetProperty("productId").GetInt64());
        Assert.Equal("POS lookup product", barcodeProduct.GetProperty("productName").GetString());
        Assert.Equal("Default", barcodeProduct.GetProperty("variantName").GetString());
        Assert.Equal("POS-VARIANT-BC", barcodeProduct.GetProperty("barcode").GetString());
        Assert.Equal("POS-LOOKUP-001", barcodeProduct.GetProperty("sku").GetString());
        Assert.Equal(22m, barcodeProduct.GetProperty("basePrice").GetDecimal());
        Assert.Equal(22m, barcodeProduct.GetProperty("effectivePrice").GetDecimal());
        Assert.Equal(10m, barcodeProduct.GetProperty("taxRate").GetDecimal());
        Assert.Equal(9, barcodeProduct.GetProperty("stockQty").GetInt32());
        Assert.True(barcodeProduct.GetProperty("inStock").GetBoolean());
        Assert.False(barcodeProduct.GetProperty("stockFromCache").GetBoolean());

        using var cachedBarcodeResponse = await client.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=5001");
        Assert.Equal(HttpStatusCode.OK, cachedBarcodeResponse.StatusCode);
        using var cachedBarcodeDocument = JsonDocument.Parse(await cachedBarcodeResponse.Content.ReadAsStringAsync());
        Assert.False(cachedBarcodeDocument.RootElement.GetProperty("data").GetProperty("stockFromCache").GetBoolean());

        using var skuResponse = await client.GetAsync(
            "/api/pos/lookup?q=POS-LOOKUP-001&outletId=5001&mode=sku");
        Assert.Equal(HttpStatusCode.OK, skuResponse.StatusCode);
        using var skuDocument = JsonDocument.Parse(await skuResponse.Content.ReadAsStringAsync());
        var skuProduct = skuDocument.RootElement.GetProperty("data");
        Assert.Equal(9801, skuProduct.GetProperty("variantId").GetInt64());
        Assert.Equal(9, skuProduct.GetProperty("stockQty").GetInt32());
        Assert.True(skuProduct.GetProperty("stockFromCache").GetBoolean());

        using var variantResponse = await client.GetAsync(
            "/api/pos/lookup?q=9801&outletId=5001&mode=variant");
        Assert.Equal(HttpStatusCode.OK, variantResponse.StatusCode);
        using var variantDocument = JsonDocument.Parse(await variantResponse.Content.ReadAsStringAsync());
        Assert.Equal(9801, variantDocument.RootElement.GetProperty("data").GetProperty("variantId").GetInt64());

        using var productBarcodeResponse = await client.GetAsync(
            "/api/pos/lookup?q=POS-PRODUCT-BC&outletId=5001");
        Assert.Equal(HttpStatusCode.OK, productBarcodeResponse.StatusCode);
        using var productBarcodeDocument = JsonDocument.Parse(await productBarcodeResponse.Content.ReadAsStringAsync());
        var productBarcode = productBarcodeDocument.RootElement.GetProperty("data");
        Assert.Equal(9801, productBarcode.GetProperty("variantId").GetInt64());
        Assert.Equal("POS-PRODUCT-BC", productBarcode.GetProperty("barcode").GetString());
    }

    [Fact]
    public async Task Lookup_PreservesBadRequestNotFoundAndAuthorizationContracts()
    {
        await ResetAndSeedAsync();

        using var client = CreatePosClient();

        using var emptyQueryResponse = await client.GetAsync("/api/pos/lookup?q=");
        Assert.Equal(HttpStatusCode.BadRequest, emptyQueryResponse.StatusCode);

        using var missingResponse = await client.GetAsync("/api/pos/lookup?q=POS-MISSING");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var missingDocument = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "No active product found for 'POS-MISSING'.",
            missingDocument.RootElement.GetProperty("message").GetString());

        using var unauthorizedClient = CreatePosClient("sales.view");
        using var unauthorizedResponse = await unauthorizedClient.GetAsync("/api/pos/lookup?q=POS-VARIANT-BC");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);
    }

    [Fact]
    public async Task Lookup_RejectsUnauthorizedSameBusinessAndCrossBusinessOutlets()
    {
        await ResetAndSeedAsync();

        using var client = CreatePosClient();

        using var sameBusinessResponse = await client.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=5002");
        Assert.Equal(HttpStatusCode.Forbidden, sameBusinessResponse.StatusCode);
        using var sameBusinessDocument = JsonDocument.Parse(await sameBusinessResponse.Content.ReadAsStringAsync());
        Assert.False(sameBusinessDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.DoesNotContain("POS lookup product", sameBusinessDocument.RootElement.GetRawText());

        using var crossBusinessResponse = await client.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=6001");
        Assert.Equal(HttpStatusCode.Forbidden, crossBusinessResponse.StatusCode);
        using var crossBusinessDocument = JsonDocument.Parse(await crossBusinessResponse.Content.ReadAsStringAsync());
        Assert.False(crossBusinessDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.DoesNotContain("POS lookup product", crossBusinessDocument.RootElement.GetRawText());
    }

    [Fact]
    public async Task Lookup_UsesEffectiveOutletAndSharedBusinessOwnerAndSuperAdminAccess()
    {
        await ResetAndSeedAsync();

        using var switchedClient = CreatePosClient(
            roleSwitched: true,
            effectiveRole: "Cashier",
            actingOutletId: 5002);
        using var bypassAttempt = await switchedClient.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=5001");
        Assert.Equal(HttpStatusCode.Forbidden, bypassAttempt.StatusCode);

        using var effectiveOutletResponse = await switchedClient.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=5002");
        Assert.Equal(HttpStatusCode.OK, effectiveOutletResponse.StatusCode);
        using var effectiveOutletDocument = JsonDocument.Parse(await effectiveOutletResponse.Content.ReadAsStringAsync());
        Assert.Equal(30m, effectiveOutletDocument.RootElement.GetProperty("data").GetProperty("effectivePrice").GetDecimal());
        Assert.Equal(4, effectiveOutletDocument.RootElement.GetProperty("data").GetProperty("stockQty").GetInt32());

        using var ownerClient = CreatePosClient(userId: 7002, role: SystemRoleNames.BusinessOwner);
        using var ownerOutletResponse = await ownerClient.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=5002");
        Assert.Equal(HttpStatusCode.OK, ownerOutletResponse.StatusCode);

        using var ownerCrossBusinessResponse = await ownerClient.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=6001");
        Assert.Equal(HttpStatusCode.Forbidden, ownerCrossBusinessResponse.StatusCode);

        using var superAdminClient = CreatePosClient(userId: 7003, role: SystemRoleNames.SuperAdmin, businessId: 2);
        using var superAdminCrossBusinessResponse = await superAdminClient.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=6001");
        Assert.Equal(HttpStatusCode.OK, superAdminCrossBusinessResponse.StatusCode);
        using var superAdminDocument = JsonDocument.Parse(await superAdminCrossBusinessResponse.Content.ReadAsStringAsync());
        Assert.Equal(40m, superAdminDocument.RootElement.GetProperty("data").GetProperty("effectivePrice").GetDecimal());
        Assert.Equal(3, superAdminDocument.RootElement.GetProperty("data").GetProperty("stockQty").GetInt32());
    }

    private async Task ModifyCatalogueAsync(Func<RetailPOSDbContext, Task> change)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await change(db);
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> LookupDataAsync(HttpClient client, long outletId)
    {
        using var response = await client.GetAsync($"/api/pos/lookup?q=POS-VARIANT-BC&outletId={outletId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Lookup_ReflectsPriceTaxRuleAndOverrideChangesRightAfterTheyAreCommitted()
    {
        await ResetAndSeedAsync();
        using var owner = CreatePosClient(userId: 7002, role: SystemRoleNames.BusinessOwner);

        var warmed = await LookupDataAsync(owner, 5001);
        Assert.Equal(22m, warmed.GetProperty("effectivePrice").GetDecimal());
        Assert.Equal(10m, warmed.GetProperty("taxRate").GetDecimal());
        // Served from the cache this time (same value) so the following assertions prove the invalidation.
        Assert.Equal(22m, (await LookupDataAsync(owner, 5001)).GetProperty("effectivePrice").GetDecimal());

        await ModifyCatalogueAsync(async db => (await db.Products.FindAsync(9101L))!.BasePrice = 30m);
        Assert.Equal(32m, (await LookupDataAsync(owner, 5001)).GetProperty("effectivePrice").GetDecimal());

        await ModifyCatalogueAsync(async db => (await db.Products.FindAsync(9101L))!.TaxRate = 5m);
        Assert.Equal(5m, (await LookupDataAsync(owner, 5001)).GetProperty("taxRate").GetDecimal());

        await ModifyCatalogueAsync(db =>
        {
            db.PriceRules.Add(new PriceRule
            {
                BusinessId = 1, Name = "Fresh campaign", RuleType = "global", Priority = 50,
                DiscountType = "fixed", DiscountValue = 2m, IsActive = true
            });
            return Task.CompletedTask;
        });
        var withRule = await LookupDataAsync(owner, 5001);
        Assert.Equal("Fresh campaign", withRule.GetProperty("appliedRuleName").GetString());
        Assert.Equal(30m, withRule.GetProperty("effectivePrice").GetDecimal());

        await ModifyCatalogueAsync(db =>
        {
            db.OutletPriceOverrides.Add(new OutletPriceOverride
            {
                BusinessId = 1, OutletId = 5001, ProductVariantId = 9801, OverrideType = "fixed", OverrideValue = 50m, IsActive = true
            });
            return Task.CompletedTask;
        });
        Assert.Equal(48m, (await LookupDataAsync(owner, 5001)).GetProperty("effectivePrice").GetDecimal());
        // The other outlet keeps its own override price; the unscoped lookup ignores outlet overrides.
        Assert.Equal(28m, (await LookupDataAsync(owner, 5002)).GetProperty("effectivePrice").GetDecimal());

        using var cartResponse = await owner.PostAsJsonAsync(
            "/api/pos/cart-prices",
            new { items = new[] { new { productVariantId = 9801, outletId = 5001, quantity = 1 } } });
        using var cartDocument = JsonDocument.Parse(await cartResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            48m,
            Assert.Single(cartDocument.RootElement.GetProperty("data").EnumerateArray()).GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public async Task PriceCacheInvalidation_NeverTouchesAnotherBusinessOrAnUnrelatedOutlet()
    {
        await ResetAndSeedAsync();
        using var scope = _factory.Services.CreateScope();
        var version = scope.ServiceProvider.GetRequiredService<IPosPricingCacheVersion>();
        var business2Before = await version.GetTokenAsync(2, 6001, CancellationToken.None);
        var outlet5001Before = await version.GetTokenAsync(1, 5001, CancellationToken.None);
        var outlet5002Before = await version.GetTokenAsync(1, 5002, CancellationToken.None);

        await ModifyCatalogueAsync(db =>
        {
            db.OutletPriceOverrides.Add(new OutletPriceOverride
            {
                BusinessId = 1, OutletId = 5002, ProductVariantId = 9801, OverrideType = "fixed", OverrideValue = 31m, IsActive = false
            });
            return Task.CompletedTask;
        });

        using var verifyScope = _factory.Services.CreateScope();
        var after = verifyScope.ServiceProvider.GetRequiredService<IPosPricingCacheVersion>();
        Assert.Equal(business2Before, await after.GetTokenAsync(2, 6001, CancellationToken.None));
        Assert.Equal(outlet5001Before, await after.GetTokenAsync(1, 5001, CancellationToken.None));
        Assert.NotEqual(outlet5002Before, await after.GetTokenAsync(1, 5002, CancellationToken.None));

        await ModifyCatalogueAsync(async db => (await db.Products.FindAsync(9101L))!.BasePrice = 21m);
        using var finalScope = _factory.Services.CreateScope();
        var final = finalScope.ServiceProvider.GetRequiredService<IPosPricingCacheVersion>();
        Assert.Equal(business2Before, await final.GetTokenAsync(2, 6001, CancellationToken.None));
        Assert.NotEqual(outlet5001Before, await final.GetTokenAsync(1, 5001, CancellationToken.None));
    }

    [Fact]
    public async Task Lookup_NeverServesAStalePriceToReadersThatStartAfterACommitEvenUnderConcurrentLoad()
    {
        await ResetAndSeedAsync();
        using var owner = CreatePosClient(userId: 7002, role: SystemRoleNames.BusinessOwner);
        await LookupDataAsync(owner, 5001);

        for (var round = 1; round <= 5; round++)
        {
            var newBase = 30m + round;
            var duringWrite = Enumerable.Range(0, 6)
                .Select(_ => Task.Run(() => LookupDataAsync(owner, 5001)))
                .ToArray();
            await ModifyCatalogueAsync(async db => (await db.Products.FindAsync(9101L))!.BasePrice = newBase);
            await Task.WhenAll(duringWrite);

            var afterCommit = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => LookupDataAsync(owner, 5001))));
            Assert.All(afterCommit, data => Assert.Equal(newBase + 2m, data.GetProperty("effectivePrice").GetDecimal()));
        }
    }

    [Fact]
    public async Task Lookup_ProductPriceChangeInOneBusinessDoesNotChangeAnotherBusinessLookup()
    {
        await ResetAndSeedAsync();
        using var business2 = CreatePosClient(userId: 7003, role: SystemRoleNames.SuperAdmin, businessId: 2);
        var before = await LookupDataAsync(business2, 6001);

        await ModifyCatalogueAsync(async db => (await db.Products.FindAsync(9101L))!.BasePrice = 99m);

        var after = await LookupDataAsync(business2, 6001);
        Assert.Equal(before.GetProperty("effectivePrice").GetDecimal(), after.GetProperty("effectivePrice").GetDecimal());
        Assert.Equal(40m, after.GetProperty("effectivePrice").GetDecimal());
    }

    [Fact]
    public async Task SuperAdmin_PosLookupAndCartPricesEnforceTheSameExplicitBusinessContext()
    {
        await ResetAndSeedAsync();
        using var noContext = TestSuperAdmin.Configure(_factory.CreateClient(), 7003, null, "sales.create");
        using var business2 = CreatePosClient(userId: 7003, role: SystemRoleNames.SuperAdmin, businessId: 2);

        using var lookupWithoutBusiness = await noContext.GetAsync("/api/pos/lookup?q=POS-VARIANT-BC&outletId=6001");
        using var cartWithoutBusiness = await noContext.PostAsJsonAsync(
            "/api/pos/cart-prices",
            new { items = new[] { new { productVariantId = 9802, outletId = 6001, quantity = 1 } } });
        using var foreignOutletLookup = await business2.GetAsync("/api/pos/lookup?q=POS-VARIANT-BC&outletId=5001");
        using var foreignOutletCart = await business2.PostAsJsonAsync(
            "/api/pos/cart-prices",
            new { items = new[] { new { productVariantId = 9801, outletId = 5001, quantity = 1 } } });

        Assert.Equal(HttpStatusCode.BadRequest, lookupWithoutBusiness.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cartWithoutBusiness.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignOutletLookup.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, foreignOutletCart.StatusCode);
        Assert.DoesNotContain("POS lookup product", await foreignOutletLookup.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Lookup_PreservesOutletOverrideAndHighestPriorityDiscountPrice()
    {
        await ResetAndSeedAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.OutletPriceOverrides.Add(new OutletPriceOverride
            {
                Id = 303,
                BusinessId = 1,
                OutletId = 5001,
                ProductVariantId = 9801,
                OverrideType = "fixed",
                OverrideValue = 26m,
                IsActive = true
            });
            db.PriceRules.AddRange(
                new PriceRule
                {
                    Id = 401,
                    BusinessId = 1,
                    Name = "Global fallback",
                    RuleType = "global",
                    DiscountType = "fixed",
                    DiscountValue = 3m,
                    MinQuantity = 1,
                    Priority = 4,
                    IsActive = true
                },
                new PriceRule
                {
                    Id = 402,
                    BusinessId = 1,
                    Name = "Outlet campaign",
                    RuleType = "outlet",
                    TargetId = 5001,
                    DiscountType = "percentage",
                    DiscountValue = 10m,
                    MinQuantity = 1,
                    Priority = 9,
                    IsActive = true
                });
            await db.SaveChangesAsync();
        }

        using var client = CreatePosClient();
        using var barcodeResponse = await client.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=5001");
        Assert.Equal(HttpStatusCode.OK, barcodeResponse.StatusCode);
        using var barcodeDocument = JsonDocument.Parse(await barcodeResponse.Content.ReadAsStringAsync());
        var barcodeResult = barcodeDocument.RootElement.GetProperty("data");
        Assert.Equal(22m, barcodeResult.GetProperty("basePrice").GetDecimal());
        Assert.Equal(23.4m, barcodeResult.GetProperty("effectivePrice").GetDecimal());
        Assert.Equal("Outlet campaign", barcodeResult.GetProperty("appliedRuleName").GetString());

        using var skuResponse = await client.GetAsync(
            "/api/pos/lookup?q=POS-LOOKUP-001&outletId=5001&mode=sku");
        Assert.Equal(HttpStatusCode.OK, skuResponse.StatusCode);
        using var skuDocument = JsonDocument.Parse(await skuResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            23.4m,
            skuDocument.RootElement.GetProperty("data").GetProperty("effectivePrice").GetDecimal());
        Assert.Equal(
            "Outlet campaign",
            skuDocument.RootElement.GetProperty("data").GetProperty("appliedRuleName").GetString());
    }

    [Fact]
    public async Task StockHint_PreservesOutletQuantityCacheAndRequestedQuantityProjection()
    {
        await ResetAndSeedAsync();

        using var client = CreatePosClient();

        using var firstResponse = await client.GetAsync(
            "/api/pos/stock-hint?variantId=9801&outletId=5001&qty=9");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var firstDocument = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        var firstHint = firstDocument.RootElement.GetProperty("data");
        Assert.Equal(9801, firstHint.GetProperty("variantId").GetInt64());
        Assert.Equal(5001, firstHint.GetProperty("outletId").GetInt64());
        Assert.Equal(9, firstHint.GetProperty("availableQty").GetInt32());
        Assert.True(firstHint.GetProperty("sufficient").GetBoolean());
        Assert.False(firstHint.GetProperty("fromCache").GetBoolean());

        using var cachedResponse = await client.GetAsync(
            "/api/pos/stock-hint?variantId=9801&outletId=5001&qty=10");
        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        using var cachedDocument = JsonDocument.Parse(await cachedResponse.Content.ReadAsStringAsync());
        var cachedHint = cachedDocument.RootElement.GetProperty("data");
        Assert.Equal(9, cachedHint.GetProperty("availableQty").GetInt32());
        Assert.False(cachedHint.GetProperty("sufficient").GetBoolean());
        Assert.True(cachedHint.GetProperty("fromCache").GetBoolean());

        using var missingOutletResponse = await client.GetAsync(
            "/api/pos/stock-hint?variantId=9801&outletId=5999");
        Assert.Equal(HttpStatusCode.Forbidden, missingOutletResponse.StatusCode);
        Assert.DoesNotContain(
            "availableQty",
            await missingOutletResponse.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        using var clampedQuantityResponse = await client.GetAsync(
            "/api/pos/stock-hint?variantId=9801&outletId=5001&qty=0");
        Assert.Equal(HttpStatusCode.OK, clampedQuantityResponse.StatusCode);
        using var clampedQuantityDocument = JsonDocument.Parse(
            await clampedQuantityResponse.Content.ReadAsStringAsync());
        var clampedQuantityHint = clampedQuantityDocument.RootElement.GetProperty("data");
        Assert.True(clampedQuantityHint.GetProperty("sufficient").GetBoolean());
        Assert.True(clampedQuantityHint.GetProperty("fromCache").GetBoolean());
    }

    [Fact]
    public void StockHint_OpenApiRouteAndQueryParametersRemainUnchanged()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var path = swagger.Paths.Single(item =>
            string.Equals(item.Key, "/api/pos/stock-hint", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(OperationType.Get, path.Value.Operations.Keys);

        var parameters = path.Value.Operations[OperationType.Get].Parameters;
        Assert.Contains(parameters, parameter =>
            parameter.Name == "variantId" && parameter.In == ParameterLocation.Query);
        Assert.Contains(parameters, parameter =>
            parameter.Name == "outletId" && parameter.In == ParameterLocation.Query);
        Assert.Contains(parameters, parameter =>
            parameter.Name == "qty" && parameter.In == ParameterLocation.Query);
    }

    [Fact]
    public async Task PriceRuleReads_PreserveFilteringOrderingPagingDetailAndNotFoundContracts()
    {
        await ResetAndSeedAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.PriceRules.AddRange(
                new PriceRule
                {
                    Id = 100,
                    BusinessId = 1,
                    Name = "First global rule",
                    Description = "First description",
                    RuleType = "global",
                    DiscountType = "percentage",
                    DiscountValue = 5m,
                    MinQuantity = 1,
                    Priority = 10,
                    IsActive = true,
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new PriceRule
                {
                    Id = 101,
                    BusinessId = 1,
                    Name = "Second global rule",
                    Description = "Second description",
                    RuleType = "global",
                    DiscountType = "fixed",
                    DiscountValue = 2m,
                    MinQuantity = 2,
                    Priority = 10,
                    IsActive = true,
                    CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
                },
                new PriceRule
                {
                    Id = 102,
                    BusinessId = 1,
                    Name = "Inactive global rule",
                    RuleType = "global",
                    DiscountType = "percentage",
                    DiscountValue = 20m,
                    Priority = 100,
                    IsActive = false
                },
                new PriceRule
                {
                    Id = 103,
                    BusinessId = 1,
                    Name = "Product rule",
                    RuleType = "product",
                    TargetId = 9101,
                    DiscountType = "percentage",
                    DiscountValue = 15m,
                    Priority = 100,
                    IsActive = true
                });
            await db.SaveChangesAsync();
        }

        using var client = CreatePosClient(permissions: "sales.create,products.view");
        using var firstPageResponse = await client.PostAsJsonAsync(
            "/api/pricing/rules/search",
            new { ruleType = "global", isActive = true, pageNumber = 1, pageSize = 1 });
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        using var firstPageDocument = JsonDocument.Parse(
            await firstPageResponse.Content.ReadAsStringAsync());
        var firstPage = firstPageDocument.RootElement.GetProperty("data");
        Assert.Equal(2, firstPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, firstPage.GetProperty("pageNumber").GetInt32());
        Assert.Equal(1, firstPage.GetProperty("pageSize").GetInt32());
        Assert.Equal(101, firstPage.GetProperty("rules")[0].GetProperty("id").GetInt64());
        Assert.Equal("Second global rule", firstPage.GetProperty("rules")[0].GetProperty("name").GetString());
        Assert.Equal("fixed", firstPage.GetProperty("rules")[0].GetProperty("discountType").GetString());

        using var secondPageResponse = await client.PostAsJsonAsync(
            "/api/pricing/rules/search",
            new { ruleType = "global", isActive = true, pageNumber = 2, pageSize = 1 });
        Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);
        using var secondPageDocument = JsonDocument.Parse(
            await secondPageResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            100,
            secondPageDocument.RootElement.GetProperty("data").GetProperty("rules")[0].GetProperty("id").GetInt64());

        using var detailResponse = await client.GetAsync("/api/pricing/rules/101");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detailDocument = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        var detail = detailDocument.RootElement.GetProperty("data");
        Assert.Equal("Second global rule", detail.GetProperty("name").GetString());
        Assert.Equal("Second description", detail.GetProperty("description").GetString());
        Assert.Equal(2, detail.GetProperty("minQuantity").GetInt32());
        Assert.Equal(10, detail.GetProperty("priority").GetInt32());

        using var missingResponse = await client.GetAsync("/api/pricing/rules/999");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var missingDocument = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Price rule with ID 999 not found",
            missingDocument.RootElement.GetProperty("message").GetString());

        using var unauthorizedClient = CreatePosClient(permissions: "sales.create");
        using var unauthorizedResponse = await unauthorizedClient.GetAsync("/api/pricing/rules/101");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);
    }

    [Fact]
    public void PriceRuleReads_OpenApiRoutesAndParametersRemainUnchanged()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Contains(OperationType.Post, swagger.Paths["/api/pricing/rules/search"].Operations.Keys);
        var detail = swagger.Paths["/api/pricing/rules/{id}"].Operations[OperationType.Get];
        Assert.Contains(detail.Parameters, parameter =>
            parameter.Name == "id" && parameter.In == ParameterLocation.Path);
    }

    [Fact]
    public async Task PriceRuleCommands_PreserveCreateUpdateDeleteNormalizationAndResponseContracts()
    {
        await ResetAndSeedAsync();

        using var client = CreatePosClient(
            permissions: "sales.create,products.view,products.edit");
        using var createResponse = await client.PostAsJsonAsync(
            "/api/pricing/rules",
            new
            {
                name = "  Spring Rule  ",
                description = "  seasonal offer  ",
                ruleType = "GLOBAL",
                targetId = (long?)null,
                discountType = "PERCENTAGE",
                discountValue = 12.5m,
                minQuantity = 2,
                priority = 7,
                validFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                validTo = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                isActive = true
            });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        using var createdDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var created = createdDocument.RootElement.GetProperty("data");
        var id = created.GetProperty("id").GetInt64();
        Assert.Equal("Spring Rule", created.GetProperty("name").GetString());
        Assert.Equal("seasonal offer", created.GetProperty("description").GetString());
        Assert.Equal("global", created.GetProperty("ruleType").GetString());
        Assert.Equal("percentage", created.GetProperty("discountType").GetString());
        Assert.Equal(12.5m, created.GetProperty("discountValue").GetDecimal());
        Assert.Equal(2, created.GetProperty("minQuantity").GetInt32());
        Assert.Equal(7, created.GetProperty("priority").GetInt32());

        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/pricing/rules/{id}",
            new
            {
                name = "  Updated Rule  ",
                description = "  revised offer  ",
                ruleType = "PRODUCT",
                targetId = 9101,
                discountType = "FIXED",
                discountValue = 3m,
                minQuantity = 4,
                priority = 9,
                validFrom = (DateTime?)null,
                validTo = (DateTime?)null,
                isActive = false
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updatedDocument = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        var updated = updatedDocument.RootElement.GetProperty("data");
        Assert.Equal("Updated Rule", updated.GetProperty("name").GetString());
        Assert.Equal("revised offer", updated.GetProperty("description").GetString());
        Assert.Equal("product", updated.GetProperty("ruleType").GetString());
        Assert.Equal(9101, updated.GetProperty("targetId").GetInt64());
        Assert.Equal("fixed", updated.GetProperty("discountType").GetString());
        Assert.Equal(3m, updated.GetProperty("discountValue").GetDecimal());
        Assert.Equal(4, updated.GetProperty("minQuantity").GetInt32());
        Assert.Equal(9, updated.GetProperty("priority").GetInt32());
        Assert.False(updated.GetProperty("isActive").GetBoolean());
        Assert.Equal("Price rule updated successfully", updatedDocument.RootElement.GetProperty("message").GetString());

        using var deleteResponse = await client.DeleteAsync($"/api/pricing/rules/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        using var deletedDocument = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        Assert.Equal("Price rule deleted successfully", deletedDocument.RootElement.GetProperty("message").GetString());

        using var missingResponse = await client.GetAsync($"/api/pricing/rules/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task PriceRuleCommands_PreserveValidationDuplicateAndPermissionBehavior()
    {
        await ResetAndSeedAsync();

        using var client = CreatePosClient(
            permissions: "sales.create,products.view,products.edit");
        using var invalidResponse = await client.PostAsJsonAsync(
            "/api/pricing/rules",
            new
            {
                name = "Invalid",
                ruleType = "unknown",
                discountType = "percentage",
                discountValue = 10m,
                minQuantity = 1,
                isActive = true
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        using var invalidDocument = JsonDocument.Parse(await invalidResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Invalid RuleType 'unknown'. Must be one of: variant, product, category, outlet, global",
            invalidDocument.RootElement.GetProperty("message").GetString());

        var validRule = new
        {
            name = "Discount",
            description = (string?)null,
            ruleType = "global",
            targetId = (long?)null,
            discountType = "percentage",
            discountValue = 10m,
            minQuantity = 1,
            priority = 0,
            validFrom = (DateTime?)null,
            validTo = (DateTime?)null,
            isActive = true
        };
        using var createResponse = await client.PostAsJsonAsync("/api/pricing/rules", validRule);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var duplicateResponse = await client.PostAsJsonAsync(
            "/api/pricing/rules",
            new
            {
                validRule.name,
                validRule.description,
                validRule.ruleType,
                validRule.targetId,
                validRule.discountType,
                validRule.discountValue,
                validRule.minQuantity,
                validRule.priority,
                validRule.validFrom,
                validRule.validTo,
                validRule.isActive
            });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        using var duplicateDocument = JsonDocument.Parse(await duplicateResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "A price rule named 'Discount' already exists",
            duplicateDocument.RootElement.GetProperty("message").GetString());

        using var readOnlyClient = CreatePosClient(permissions: "sales.create,products.view");
        using var forbiddenResponse = await readOnlyClient.PostAsJsonAsync(
            "/api/pricing/rules",
            validRule);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task OutletPriceOverrideCommands_PreserveCrudNormalizationAndResponseContracts()
    {
        await ResetAndSeedAsync();

        using var client = CreatePosClient(
            permissions: "sales.create,products.view,products.edit");
        using var createResponse = await client.PostAsJsonAsync(
            "/api/pricing/outlet-overrides",
            new
            {
                outletId = 5001,
                productVariantId = 9801,
                overrideType = "FIXED",
                overrideValue = 27.5m,
                isActive = true
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        using var createdDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var created = createdDocument.RootElement.GetProperty("data");
        var id = created.GetProperty("id").GetInt64();
        Assert.Equal(5001, created.GetProperty("outletId").GetInt64());
        Assert.Equal("POS test outlet", created.GetProperty("outletName").GetString());
        Assert.Equal(9801, created.GetProperty("productVariantId").GetInt64());
        Assert.Equal("Default", created.GetProperty("variantName").GetString());
        Assert.Equal("POS lookup product", created.GetProperty("productName").GetString());
        Assert.Equal("fixed", created.GetProperty("overrideType").GetString());
        Assert.Equal(27.5m, created.GetProperty("overrideValue").GetDecimal());
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Equal(
            "Outlet price override created successfully",
            createdDocument.RootElement.GetProperty("message").GetString());

        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/pricing/outlet-overrides/{id}",
            new
            {
                overrideType = "MARGIN",
                overrideValue = 18m,
                isActive = false
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updatedDocument = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        var updated = updatedDocument.RootElement.GetProperty("data");
        Assert.Equal("margin", updated.GetProperty("overrideType").GetString());
        Assert.Equal(18m, updated.GetProperty("overrideValue").GetDecimal());
        Assert.False(updated.GetProperty("isActive").GetBoolean());
        Assert.Equal(
            "Outlet price override updated successfully",
            updatedDocument.RootElement.GetProperty("message").GetString());

        using var deleteResponse = await client.DeleteAsync(
            $"/api/pricing/outlet-overrides/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        using var deletedDocument = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Outlet price override deleted successfully",
            deletedDocument.RootElement.GetProperty("message").GetString());

        using var missingResponse = await client.GetAsync($"/api/pricing/outlet-overrides/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async Task OutletPriceOverrideCommands_PreserveValidationDuplicateAndPermissionBehavior()
    {
        await ResetAndSeedAsync();

        using var client = CreatePosClient(
            permissions: "sales.create,products.view,products.edit");
        using var invalidTypeResponse = await client.PostAsJsonAsync(
            "/api/pricing/outlet-overrides",
            new
            {
                outletId = 5001,
                productVariantId = 9801,
                overrideType = "discount",
                overrideValue = 12m,
                isActive = true
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidTypeResponse.StatusCode);
        using var invalidTypeDocument = JsonDocument.Parse(
            await invalidTypeResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Invalid OverrideType 'discount'. Must be 'fixed' or 'margin'",
            invalidTypeDocument.RootElement.GetProperty("message").GetString());

        using var missingOutletResponse = await client.PostAsJsonAsync(
            "/api/pricing/outlet-overrides",
            new
            {
                outletId = 9999,
                productVariantId = 9999,
                overrideType = "fixed",
                overrideValue = 12m,
                isActive = true
            });
        Assert.Equal(HttpStatusCode.Forbidden, missingOutletResponse.StatusCode);

        using var missingVariantResponse = await client.PostAsJsonAsync(
            "/api/pricing/outlet-overrides",
            new
            {
                outletId = 5001,
                productVariantId = 9999,
                overrideType = "fixed",
                overrideValue = 12m,
                isActive = true
            });
        Assert.Equal(HttpStatusCode.NotFound, missingVariantResponse.StatusCode);
        using var missingVariantDocument = JsonDocument.Parse(
            await missingVariantResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "ProductVariant with ID 9999 not found",
            missingVariantDocument.RootElement.GetProperty("message").GetString());

        using var ownerClient = CreatePosClient(
            permissions: "sales.create,products.view,products.edit",
            userId: 7002,
            role: SystemRoleNames.BusinessOwner);
        using var duplicateResponse = await ownerClient.PostAsJsonAsync(
            "/api/pricing/outlet-overrides",
            new
            {
                outletId = 5002,
                productVariantId = 9801,
                overrideType = "fixed",
                overrideValue = 12m,
                isActive = true
            });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        using var duplicateDocument = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "An override already exists for outlet 5002 and variant 9801. Update the existing override instead.",
            duplicateDocument.RootElement.GetProperty("message").GetString());

        using var missingUpdateResponse = await client.PutAsJsonAsync(
            "/api/pricing/outlet-overrides/9999",
            new { overrideType = "invalid", overrideValue = 12m, isActive = true });
        Assert.Equal(HttpStatusCode.NotFound, missingUpdateResponse.StatusCode);
        using var missingUpdateDocument = JsonDocument.Parse(
            await missingUpdateResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Outlet price override with ID 9999 not found",
            missingUpdateDocument.RootElement.GetProperty("message").GetString());

        using var invalidUpdateResponse = await ownerClient.PutAsJsonAsync(
            "/api/pricing/outlet-overrides/1",
            new { overrideType = "invalid", overrideValue = 12m, isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdateResponse.StatusCode);

        using var deleteMissingResponse = await client.DeleteAsync(
            "/api/pricing/outlet-overrides/9999");
        Assert.Equal(HttpStatusCode.NotFound, deleteMissingResponse.StatusCode);

        using var readOnlyClient = CreatePosClient(permissions: "sales.create,products.view");
        using var forbiddenResponse = await readOnlyClient.PostAsJsonAsync(
            "/api/pricing/outlet-overrides",
            new
            {
                outletId = 5001,
                productVariantId = 9801,
                overrideType = "fixed",
                overrideValue = 12m,
                isActive = true
            });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        using var crossOutletCreateResponse = await client.PostAsJsonAsync(
            "/api/pricing/outlet-overrides",
            new
            {
                outletId = 5002,
                productVariantId = 9801,
                overrideType = "fixed",
                overrideValue = 12m,
                isActive = true
            });
        Assert.Equal(HttpStatusCode.Forbidden, crossOutletCreateResponse.StatusCode);
        using var crossOutletUpdateResponse = await client.PutAsJsonAsync(
            "/api/pricing/outlet-overrides/1",
            new { overrideType = "margin", overrideValue = 12m, isActive = true });
        Assert.Equal(HttpStatusCode.Forbidden, crossOutletUpdateResponse.StatusCode);
        using var crossOutletDeleteResponse = await client.DeleteAsync(
            "/api/pricing/outlet-overrides/1");
        Assert.Equal(HttpStatusCode.Forbidden, crossOutletDeleteResponse.StatusCode);
    }

    [Fact]
    public void OutletPriceOverrideCommands_OpenApiRoutesAndParametersRemainUnchanged()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Contains(
            OperationType.Post,
            swagger.Paths["/api/pricing/outlet-overrides"].Operations.Keys);
        var update = swagger.Paths["/api/pricing/outlet-overrides/{id}"];
        Assert.Contains(OperationType.Put, update.Operations.Keys);
        Assert.Contains(
            update.Operations[OperationType.Put].Parameters,
            parameter => parameter.Name == "id" && parameter.In == ParameterLocation.Path);
        Assert.Contains(OperationType.Delete, update.Operations.Keys);
    }

    [Fact]
    public async Task OutletPriceOverrideReads_PreserveOutletFilterProjectionAndNotFoundContracts()
    {
        await ResetAndSeedAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.OutletPriceOverrides.AddRange(
                new OutletPriceOverride
                {
                    Id = 301,
                    BusinessId = 1,
                    OutletId = 5001,
                    ProductVariantId = 9801,
                    OverrideType = "fixed",
                    OverrideValue = 25m,
                    IsActive = true,
                    CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new OutletPriceOverride
                {
                    Id = 302,
                    BusinessId = 1,
                    OutletId = 5002,
                    ProductVariantId = 9801,
                    OverrideType = "margin",
                    OverrideValue = 15m,
                    IsActive = false
                });
            await db.SaveChangesAsync();
        }

        using var client = CreatePosClient(permissions: "sales.create,products.view");
        using var listResponse = await client.GetAsync(
            "/api/pricing/outlet-overrides/outlet/5001");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var list = listDocument.RootElement.GetProperty("data");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        var listedOverride = list.GetProperty("overrides")[0];
        Assert.Equal(301, listedOverride.GetProperty("id").GetInt64());
        Assert.Equal(string.Empty, listedOverride.GetProperty("outletName").GetString());
        Assert.Equal("Default", listedOverride.GetProperty("variantName").GetString());
        Assert.Equal("POS lookup product", listedOverride.GetProperty("productName").GetString());
        Assert.Equal(25m, listedOverride.GetProperty("overrideValue").GetDecimal());

        using var detailResponse = await client.GetAsync("/api/pricing/outlet-overrides/301");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detailDocument = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "fixed",
            detailDocument.RootElement.GetProperty("data").GetProperty("overrideType").GetString());
        Assert.Equal(
            "POS test outlet",
            detailDocument.RootElement.GetProperty("data").GetProperty("outletName").GetString());

        using var missingResponse = await client.GetAsync("/api/pricing/outlet-overrides/999");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var missingDocument = JsonDocument.Parse(await missingResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "Outlet price override with ID 999 not found",
            missingDocument.RootElement.GetProperty("message").GetString());

        using var unauthorizedClient = CreatePosClient(permissions: "sales.create");
        using var unauthorizedResponse = await unauthorizedClient.GetAsync(
            "/api/pricing/outlet-overrides/outlet/5001");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);
    }

    [Fact]
    public void OutletPriceOverrideReads_OpenApiRoutesAndParametersRemainUnchanged()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var byOutlet = swagger.Paths["/api/pricing/outlet-overrides/outlet/{outletId}"]
            .Operations[OperationType.Get];
        Assert.Contains(byOutlet.Parameters, parameter =>
            parameter.Name == "outletId" && parameter.In == ParameterLocation.Path);

        var byId = swagger.Paths["/api/pricing/outlet-overrides/{id}"]
            .Operations[OperationType.Get];
        Assert.Contains(byId.Parameters, parameter =>
            parameter.Name == "id" && parameter.In == ParameterLocation.Path);
    }

    [Fact]
    public async Task Lookup_CacheSeparatesOutletAndBusinessDataAndOmittedOutletRemainsUnscoped()
    {
        await ResetAndSeedAsync();

        using var ownerClient = CreatePosClient(userId: 7002, role: SystemRoleNames.BusinessOwner);

        using var firstOutletResponse = await ownerClient.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=5001");
        Assert.Equal(HttpStatusCode.OK, firstOutletResponse.StatusCode);
        using var firstOutletDocument = JsonDocument.Parse(await firstOutletResponse.Content.ReadAsStringAsync());

        using var secondOutletResponse = await ownerClient.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=5002");
        Assert.Equal(HttpStatusCode.OK, secondOutletResponse.StatusCode);
        using var secondOutletDocument = JsonDocument.Parse(await secondOutletResponse.Content.ReadAsStringAsync());

        var firstOutlet = firstOutletDocument.RootElement.GetProperty("data");
        var secondOutlet = secondOutletDocument.RootElement.GetProperty("data");
        Assert.Equal(22m, firstOutlet.GetProperty("effectivePrice").GetDecimal());
        Assert.Equal(9, firstOutlet.GetProperty("stockQty").GetInt32());
        Assert.Equal(30m, secondOutlet.GetProperty("effectivePrice").GetDecimal());
        Assert.Equal(4, secondOutlet.GetProperty("stockQty").GetInt32());

        using var noOutletResponse = await ownerClient.GetAsync("/api/pos/lookup?q=POS-VARIANT-BC");
        Assert.Equal(HttpStatusCode.OK, noOutletResponse.StatusCode);
        using var noOutletDocument = JsonDocument.Parse(await noOutletResponse.Content.ReadAsStringAsync());
        var noOutlet = noOutletDocument.RootElement.GetProperty("data");
        Assert.Equal(22m, noOutlet.GetProperty("effectivePrice").GetDecimal());
        Assert.Equal(0, noOutlet.GetProperty("stockQty").GetInt32());
        Assert.False(noOutlet.GetProperty("stockFromCache").GetBoolean());

        using var warmedForeignOutlet = CreatePosClient(userId: 7003, role: SystemRoleNames.SuperAdmin, businessId: 2);
        using var foreignWarmResponse = await warmedForeignOutlet.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=6001");
        Assert.Equal(HttpStatusCode.OK, foreignWarmResponse.StatusCode);
        using var restrictedClient = CreatePosClient();
        using var cachedCrossBusinessAttempt = await restrictedClient.GetAsync(
            "/api/pos/lookup?q=POS-VARIANT-BC&outletId=6001");
        Assert.Equal(HttpStatusCode.Forbidden, cachedCrossBusinessAttempt.StatusCode);
        using var rejectedDocument = JsonDocument.Parse(await cachedCrossBusinessAttempt.Content.ReadAsStringAsync());
        Assert.DoesNotContain("POS lookup product", rejectedDocument.RootElement.GetRawText());
    }

    [Fact]
    public async Task CartPrices_ValidatesEverySuppliedOutletBeforeReturningPrices()
    {
        await ResetAndSeedAsync();

        using var restrictedClient = CreatePosClient();

        using var sameBusinessResponse = await restrictedClient.PostAsJsonAsync(
            "/api/pos/cart-prices",
            new { items = new[] { new { productVariantId = 9801, outletId = 5002, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.Forbidden, sameBusinessResponse.StatusCode);
        using var sameBusinessDocument = JsonDocument.Parse(await sameBusinessResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain("POS lookup product", sameBusinessDocument.RootElement.GetRawText());

        using var crossBusinessResponse = await restrictedClient.PostAsJsonAsync(
            "/api/pos/cart-prices",
            new { items = new[] { new { productVariantId = 9801, outletId = 6001, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.Forbidden, crossBusinessResponse.StatusCode);

        using var mixedBatchResponse = await restrictedClient.PostAsJsonAsync(
            "/api/pos/cart-prices",
            new
            {
                items = new[]
                {
                    new { productVariantId = 9801, outletId = 5001, quantity = 1 },
                    new { productVariantId = 9801, outletId = 5002, quantity = 1 }
                }
            });
        Assert.Equal(HttpStatusCode.Forbidden, mixedBatchResponse.StatusCode);
        using var mixedBatchDocument = JsonDocument.Parse(await mixedBatchResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain("POS lookup product", mixedBatchDocument.RootElement.GetRawText());

        using var ownerClient = CreatePosClient(userId: 7002, role: SystemRoleNames.BusinessOwner);
        using var ownerResponse = await ownerClient.PostAsJsonAsync(
            "/api/pos/cart-prices",
            new { items = new[] { new { productVariantId = 9801, outletId = 5002, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        using var ownerDocument = JsonDocument.Parse(await ownerResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            30m,
            Assert.Single(ownerDocument.RootElement.GetProperty("data").EnumerateArray())
                .GetProperty("finalPrice").GetDecimal());

        using var priceClient = CreatePosClient(
            permissions: "sales.create,products.view",
            userId: 7002,
            role: SystemRoleNames.BusinessOwner);
        using var cachedPriceResponse = await priceClient.PostAsJsonAsync(
            "/api/pricing/calculate",
            new { productVariantId = 9801, outletId = 5002, quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, cachedPriceResponse.StatusCode);
        using var cachedPriceDocument = JsonDocument.Parse(await cachedPriceResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            30m,
            cachedPriceDocument.RootElement.GetProperty("data").GetProperty("finalPrice").GetDecimal());

        using var superAdminClient = CreatePosClient(userId: 7003, role: SystemRoleNames.SuperAdmin, businessId: 2);
        using var superAdminResponse = await superAdminClient.PostAsJsonAsync(
            "/api/pos/cart-prices",
            new { items = new[] { new { productVariantId = 9802, outletId = 6001, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.OK, superAdminResponse.StatusCode);
        using var superAdminDocument = JsonDocument.Parse(await superAdminResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            40m,
            Assert.Single(superAdminDocument.RootElement.GetProperty("data").EnumerateArray())
                .GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public async Task PricingCalculationRoutesAuthorizeEveryOutletBeforeCalculationOrCacheAccess()
    {
        await ResetAndSeedAsync();

        using var restrictedClient = CreatePosClient(permissions: "sales.create,products.view");
        await WriteLegacyPriceCacheAsync(
            9801,
            5002,
            1,
            new PriceCalculationResultDto
            {
                ProductVariantId = 9801,
                ProductName = "must not leak from cache",
                VariantName = "Default",
                FinalPrice = 999m
            });

        using var singleResponse = await restrictedClient.PostAsJsonAsync(
            "/api/pricing/calculate",
            new { productVariantId = 9801, outletId = 5002, quantity = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, singleResponse.StatusCode);
        Assert.DoesNotContain(
            "must not leak from cache",
            await singleResponse.Content.ReadAsStringAsync());

        using var batchResponse = await restrictedClient.PostAsJsonAsync(
            "/api/pricing/calculate-batch",
            new
            {
                items = new[]
                {
                    new { productVariantId = 9801, outletId = 5001, quantity = 1 },
                    new { productVariantId = 9801, outletId = 5002, quantity = 1 }
                }
            });
        Assert.Equal(HttpStatusCode.Forbidden, batchResponse.StatusCode);
        Assert.DoesNotContain(
            "must not leak from cache",
            await batchResponse.Content.ReadAsStringAsync());

        using var getResponse = await restrictedClient.GetAsync(
            "/api/pricing/product/9801?outletId=6001&quantity=1");
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

        using var switchedClient = CreatePosClient(
            permissions: "sales.create,products.view",
            roleSwitched: true,
            effectiveRole: "Cashier",
            actingOutletId: 5002);
        using var switchedDenied = await switchedClient.PostAsJsonAsync(
            "/api/pricing/calculate",
            new { productVariantId = 9801, outletId = 5001, quantity = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, switchedDenied.StatusCode);

        using var switchedAllowed = await switchedClient.PostAsJsonAsync(
            "/api/pricing/calculate",
            new { productVariantId = 9801, outletId = 5002, quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, switchedAllowed.StatusCode);
    }

    [Fact]
    public async Task PricingCalculationRoutesPreserveProjectionOutletAndCacheBehavior()
    {
        await ResetAndSeedAsync();

        using var ownerClient = CreatePosClient(
            permissions: "sales.create,products.view",
            userId: 7002,
            role: SystemRoleNames.BusinessOwner);

        using var noOutletResponse = await ownerClient.PostAsJsonAsync(
            "/api/pricing/calculate",
            new { productVariantId = 9801, quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, noOutletResponse.StatusCode);
        using var noOutletDocument = JsonDocument.Parse(await noOutletResponse.Content.ReadAsStringAsync());
        var noOutletData = noOutletDocument.RootElement.GetProperty("data");
        Assert.Equal(22m, noOutletData.GetProperty("basePrice").GetDecimal());
        Assert.Equal(22m, noOutletData.GetProperty("finalPrice").GetDecimal());

        using var outletResponse = await ownerClient.GetAsync(
            "/api/pricing/product/9801?outletId=5002&quantity=2");
        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        using var outletDocument = JsonDocument.Parse(await outletResponse.Content.ReadAsStringAsync());
        var outletData = outletDocument.RootElement.GetProperty("data");
        Assert.Equal(30m, outletData.GetProperty("outletPrice").GetDecimal());
        Assert.Equal(10m, outletData.GetProperty("taxRate").GetDecimal());
        Assert.Equal(33m, outletData.GetProperty("finalPriceWithTax").GetDecimal());
        Assert.Contains("breakdown", outletData.EnumerateObject().Select(property => property.Name));

        using var foreignOutletResponse = await ownerClient.GetAsync(
            "/api/pricing/product/9801?outletId=6001&quantity=2");
        Assert.Equal(HttpStatusCode.Forbidden, foreignOutletResponse.StatusCode);

        using var superAdminClient = CreatePosClient(
            permissions: "sales.create,products.view",
            userId: 7003,
            role: SystemRoleNames.SuperAdmin,
            businessId: 2);
        using var superAdminResponse = await superAdminClient.GetAsync(
            "/api/pricing/product/9802?outletId=6001&quantity=1");
        Assert.Equal(HttpStatusCode.OK, superAdminResponse.StatusCode);
        using var superAdminDocument = JsonDocument.Parse(await superAdminResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            40m,
            superAdminDocument.RootElement.GetProperty("data").GetProperty("finalPrice").GetDecimal());

        using var batchResponse = await ownerClient.PostAsJsonAsync(
            "/api/pricing/calculate-batch",
            new
            {
                items = new[]
                {
                    new { productVariantId = 9801, outletId = 5001, quantity = 1 },
                    new { productVariantId = 9801, outletId = 5002, quantity = 1 }
                }
            });
        Assert.Equal(HttpStatusCode.OK, batchResponse.StatusCode);
        using var batchDocument = JsonDocument.Parse(await batchResponse.Content.ReadAsStringAsync());
        var prices = batchDocument.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, prices.Length);
        Assert.Equal(22m, prices[0].GetProperty("finalPrice").GetDecimal());
        Assert.Equal(30m, prices[1].GetProperty("finalPrice").GetDecimal());

        using var cacheHitResponse = await ownerClient.PostAsJsonAsync(
            "/api/pricing/calculate",
            new { productVariantId = 9801, outletId = 5002, quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, cacheHitResponse.StatusCode);
        using var cacheHitDocument = JsonDocument.Parse(await cacheHitResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            30m,
            cacheHitDocument.RootElement.GetProperty("data").GetProperty("finalPrice").GetDecimal());

        await WriteLegacyPriceCacheAsync(
            9801,
            5002,
            7,
            new PriceCalculationResultDto
            {
                ProductVariantId = 9801,
                ProductName = "legacy price cache product",
                VariantName = "Default",
                BasePrice = 22m,
                OutletPrice = 30m,
                FinalPrice = 29m,
                TaxRate = 10m,
                TaxAmount = 2.9m,
                FinalPriceWithTax = 31.9m,
                Breakdown = [new PriceBreakdownLineDto { Label = "Cached", Amount = 29m }]
            });
        using var legacyCacheResponse = await ownerClient.GetAsync(
            "/api/pricing/product/9801?outletId=5002&quantity=7");
        Assert.Equal(HttpStatusCode.OK, legacyCacheResponse.StatusCode);
        using var legacyCacheDocument = JsonDocument.Parse(
            await legacyCacheResponse.Content.ReadAsStringAsync());
        var legacyCachedData = legacyCacheDocument.RootElement.GetProperty("data");
        Assert.Equal("legacy price cache product", legacyCachedData.GetProperty("productName").GetString());
        Assert.Equal(31.9m, legacyCachedData.GetProperty("finalPriceWithTax").GetDecimal());
        Assert.Equal(
            "Cached",
            legacyCachedData.GetProperty("breakdown")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void Lookup_OpenApiRouteAndQueryParametersRemainUnchanged()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var path = swagger.Paths.Single(item =>
            string.Equals(item.Key, "/api/pos/lookup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(OperationType.Get, path.Value.Operations.Keys);
        var parameters = path.Value.Operations[OperationType.Get].Parameters;
        Assert.Contains(parameters, parameter => parameter.Name == "q" && parameter.In == ParameterLocation.Query);
        Assert.Contains(parameters, parameter => parameter.Name == "outletId" && parameter.In == ParameterLocation.Query);
        Assert.Contains(parameters, parameter => parameter.Name == "mode" && parameter.In == ParameterLocation.Query);
    }

    [Fact]
    public void PricingCalculation_OpenApiRoutesAndParametersRemainUnchanged()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        var calculate = swagger.Paths["/api/pricing/calculate"];
        Assert.Contains(OperationType.Post, calculate.Operations.Keys);

        var batch = swagger.Paths["/api/pricing/calculate-batch"];
        Assert.Contains(OperationType.Post, batch.Operations.Keys);

        var product = swagger.Paths["/api/pricing/product/{variantId}"];
        Assert.Contains(OperationType.Get, product.Operations.Keys);
        var operation = product.Operations[OperationType.Get];
        Assert.Contains(operation.Parameters, parameter =>
            parameter.Name == "variantId" && parameter.In == ParameterLocation.Path);
        Assert.Contains(operation.Parameters, parameter =>
            parameter.Name == "outletId" && parameter.In == ParameterLocation.Query);
        Assert.Contains(operation.Parameters, parameter =>
            parameter.Name == "quantity" && parameter.In == ParameterLocation.Query);
        Assert.Contains(operation.Responses.Keys, statusCode => statusCode == "403");
    }

    private HttpClient CreatePosClient(
        string permissions = "sales.create",
        long userId = 7001,
        string role = "Cashier",
        bool roleSwitched = false,
        string? effectiveRole = null,
        long? actingOutletId = null,
        long businessId = 1)
    {
        var client = _factory.CreateClient();
        if (string.Equals(role, SystemRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return TestSuperAdmin.Configure(client, userId, businessId, permissions);
        }

        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (roleSwitched)
        {
            client.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
            client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", effectiveRole ?? role);
        }
        if (actingOutletId.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Test-ActingOutletId", actingOutletId.Value.ToString());
        }

        return client;
    }

    private async Task WriteLegacyPriceCacheAsync(
        long variantId,
        long? outletId,
        int quantity,
        PriceCalculationResultDto value)
    {
        var cache = _factory.Services.GetRequiredService<IDistributedCache>();
        using var scope = _factory.Services.CreateScope();
        var token = await scope.ServiceProvider.GetRequiredService<IPosPricingCacheVersion>()
            .GetTokenAsync(1, outletId, CancellationToken.None);
        var json = JsonSerializer.Serialize(
            value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await cache.SetStringAsync(
            $"pos:price:{BusinessOneCacheScope}:{token}:{variantId}:{outletId ?? 0}:{quantity}",
            json);
    }

    // Business id, then the tax policy (enabled, default rate, prices include tax) that scope every cached price and lookup.
    private const string BusinessOneCacheScope = "1:1:10:0";

    // Orphans every cached price and lookup (the versioned keys) and clears the per-outlet stock hints.
    private async Task ClearPosCachesAsync(IPosCacheService posCache)
    {
        using var scope = _factory.Services.CreateScope();
        var version = scope.ServiceProvider.GetRequiredService<IPosPricingCacheVersion>();
        foreach (var businessId in new long[] { 1, 2 })
        {
            await version.InvalidateAsync(businessId, null, CancellationToken.None);
            foreach (var outletId in new long[] { 5001, 5002, 5003, 6001 })
            {
                await version.InvalidateAsync(businessId, outletId, CancellationToken.None);
            }
        }

        foreach (var outletId in new long[] { 5001, 5002, 6001 })
        {
            await posCache.InvalidateStockAsync(9802, outletId);
        }
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IPosCacheService>();

        foreach (var outletId in new long?[] { null, 5001, 5002, 6001 })
        {
            foreach (var barcode in new[] { "POS-VARIANT-BC", "POS-PRODUCT-BC" })
            {
                await cache.InvalidateLookupAsync(9801, barcode, "POS-LOOKUP-001", outletId);
            }
            await cache.InvalidatePriceAsync(9801, outletId);
            if (outletId.HasValue)
            {
                await cache.InvalidateStockAsync(9801, outletId.Value);
            }
        }

        await ClearPosCachesAsync(cache);

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(
            new Business { Id = 1, Name = "POS test business", IsActive = true },
            new Business { Id = 2, Name = "Foreign POS test business", IsActive = true });
        db.Categories.AddRange(
            new Category { Id = 9001, BusinessId = 1, Name = "POS lookup category" },
            new Category { Id = 9002, BusinessId = 2, Name = "Foreign POS lookup category" });
        db.Outlets.AddRange(
            new Outlet { Id = 5001, BusinessId = 1, Name = "POS test outlet" },
            new Outlet { Id = 5002, BusinessId = 1, Name = "Other POS outlet" },
            new Outlet { Id = 6001, BusinessId = 2, Name = "Foreign POS outlet" });
        db.Users.AddRange(
            new User
            {
                Id = 7001,
                BusinessId = 1,
                Name = "POS cashier",
                Email = "pos-cashier@example.com",
                PasswordHash = "x",
                OutletId = 5001,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
                IsActive = true
            },
            new User
            {
                Id = 7002,
                BusinessId = 1,
                Name = "POS business owner",
                Email = "pos-owner@example.com",
                PasswordHash = "x",
                OutletId = 5001,
                InventoryLocationAccessScope = string.Empty,
                IsActive = true
            });
        db.Products.AddRange(
            new Product
            {
                Id = 9101,
                BusinessId = 1,
                Name = "POS lookup product",
                Barcode = "POS-PRODUCT-BC",
                CategoryId = 9001,
                BasePrice = 20m,
                TaxRate = 10m,
                Status = ProductStatus.Active
            },
            new Product
            {
                Id = 9102,
                BusinessId = 2,
                Name = "POS lookup product",
                Barcode = "POS-PRODUCT-BC",
                CategoryId = 9002,
                BasePrice = 20m,
                TaxRate = 10m,
                Status = ProductStatus.Active
            });
        db.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = 9801,
                BusinessId = 1,
                ProductId = 9101,
                Name = "Default",
                Sku = "POS-LOOKUP-001",
                Barcode = "POS-VARIANT-BC",
                PriceAdjustment = 2m
            },
            new ProductVariant
            {
                Id = 9802,
                BusinessId = 2,
                ProductId = 9102,
                Name = "Default",
                Sku = "POS-LOOKUP-001",
                Barcode = "POS-VARIANT-BC",
                PriceAdjustment = 2m
            });
        db.Inventories.AddRange(
            new RetailPOS.Core.Entities.Inventory
            {
                Id = 9901,
                VariantId = 9801,
                LocationId = 5001,
                LocationType = "outlet",
                Quantity = 9
            },
            new RetailPOS.Core.Entities.Inventory
            {
                Id = 9902,
                VariantId = 9801,
                LocationId = 5002,
                LocationType = "outlet",
                Quantity = 4
            },
            new RetailPOS.Core.Entities.Inventory
            {
                Id = 9903,
                VariantId = 9802,
                LocationId = 6001,
                LocationType = "outlet",
                Quantity = 3
            });
        db.OutletPriceOverrides.AddRange(
            new OutletPriceOverride
            {
                Id = 1,
                BusinessId = 1,
                OutletId = 5002,
                ProductVariantId = 9801,
                OverrideType = "fixed",
                OverrideValue = 30m
            },
            new OutletPriceOverride
            {
                Id = 2,
                BusinessId = 2,
                OutletId = 6001,
                ProductVariantId = 9802,
                OverrideType = "fixed",
                OverrideValue = 40m
            });

        await db.SaveChangesAsync();
        await TestSuperAdmin.SeedAsync(db, 7003);
    }
}
