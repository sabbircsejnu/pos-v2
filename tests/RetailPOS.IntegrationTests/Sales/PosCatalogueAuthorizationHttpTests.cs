using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Sales;

/// <summary>
/// Phase 4 POS lookup authorization: POS Checkout browses, searches, filters and prices through the
/// /api/pos/* endpoints authorized by sales.create, so a seller never needs Product or Category
/// Management permissions, and those management endpoints stay exactly as protected as before.
/// </summary>
public sealed class PosCatalogueAuthorizationHttpTests : IClassFixture<RetailPosApiFactory>
{
    // A POS seller with no Product/Category Management permission (the OutletManager/Salesman shape).
    private const string SellerPermissions = "sales.create,sales.view,cash_sessions.view,cash_sessions.open,customers.view";

    private readonly RetailPosApiFactory _factory;

    public PosCatalogueAuthorizationHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SellerWithoutProductsView_LoadsOnlyActiveVariantsOfOwnBusinessWithOutletStockAndNoCost()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(SellerPermissions);

        using var response = await client.GetAsync("/api/pos/catalogue?outletId=5001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var items = document.RootElement.GetProperty("data").EnumerateArray().ToArray();

        Assert.Equal(new long[] { 9812, 9811, 9821 }, items.Select(item => item.GetProperty("variantId").GetInt64()).ToArray());
        var red = items[1];
        Assert.Equal(9111, red.GetProperty("productId").GetInt64());
        Assert.Equal("Catalogue shirt", red.GetProperty("productName").GetString());
        Assert.Equal("Red", red.GetProperty("variantName").GetString());
        Assert.Equal("CAT-SHIRT-RED", red.GetProperty("sku").GetString());
        Assert.Equal("CAT-BC-RED", red.GetProperty("barcode").GetString());
        Assert.Equal(9011, red.GetProperty("categoryId").GetInt64());
        Assert.Equal("Apparel", red.GetProperty("categoryName").GetString());
        Assert.Equal(12m, red.GetProperty("listPrice").GetDecimal());
        Assert.Equal(7, red.GetProperty("stockQty").GetInt32());
        // A variant without an inventory row at the outlet reports zero, not another outlet's quantity.
        Assert.Equal(0, items[0].GetProperty("stockQty").GetInt32());

        Assert.DoesNotContain("cost", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Foreign", body);
        Assert.DoesNotContain("Retired", body);
    }

    [Fact]
    public async Task SellerWithoutCategoriesView_LoadsOnlyActiveCategoriesOfOwnBusinessAndCanFilterBySearchAndCategory()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient(SellerPermissions);

        using var categoriesResponse = await client.GetAsync("/api/pos/categories");
        Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
        using var categoriesDocument = JsonDocument.Parse(await categoriesResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            new long[] { 9011, 9012 },
            categoriesDocument.RootElement.GetProperty("data").EnumerateArray()
                .Select(category => category.GetProperty("id").GetInt64()).ToArray());

        Assert.Equal(new long[] { 9821 }, await CatalogueVariantIdsAsync(client, "/api/pos/catalogue?outletId=5001&categoryId=9012"));
        Assert.Equal(new long[] { 9812 }, await CatalogueVariantIdsAsync(client, "/api/pos/catalogue?outletId=5001&q=cat-bc-blue"));
        Assert.Equal(new long[] { 9812, 9811 }, await CatalogueVariantIdsAsync(client, "/api/pos/catalogue?q=shirt"));
        // Another Business's category id never surfaces that Business's products.
        Assert.Empty(await CatalogueVariantIdsAsync(client, "/api/pos/catalogue?outletId=5001&categoryId=9091"));
        Assert.Equal(new long[] { 9812 }, await CatalogueVariantIdsAsync(client, "/api/pos/catalogue?limit=1"));

        using var badLimit = await client.GetAsync("/api/pos/catalogue?limit=501");
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
    }

    [Fact]
    public async Task PosCatalogueAndCategories_RequireSalesCreate()
    {
        await ResetAndSeedAsync();
        using var managementOnly = CreateClient("products.view,categories.view");

        using var catalogue = await managementOnly.GetAsync("/api/pos/catalogue?outletId=5001");
        Assert.Equal(HttpStatusCode.Forbidden, catalogue.StatusCode);
        using var categories = await managementOnly.GetAsync("/api/pos/categories");
        Assert.Equal(HttpStatusCode.Forbidden, categories.StatusCode);
    }

    [Fact]
    public async Task CatalogueOutletStock_FollowsLocationAuthorizationIncludingRoleSwitchedActingOutlet()
    {
        await ResetAndSeedAsync();
        using var seller = CreateClient(SellerPermissions);

        using var otherOutlet = await seller.GetAsync("/api/pos/catalogue?outletId=5002");
        Assert.Equal(HttpStatusCode.Forbidden, otherOutlet.StatusCode);
        Assert.DoesNotContain("Catalogue shirt", await otherOutlet.Content.ReadAsStringAsync());

        using var foreignOutlet = await seller.GetAsync("/api/pos/catalogue?outletId=6001");
        Assert.Equal(HttpStatusCode.Forbidden, foreignOutlet.StatusCode);
        Assert.DoesNotContain("Catalogue shirt", await foreignOutlet.Content.ReadAsStringAsync());

        using var switched = CreateClient(SellerPermissions, roleSwitched: true, actingOutletId: 5002);
        using var bypass = await switched.GetAsync("/api/pos/catalogue?outletId=5001");
        Assert.Equal(HttpStatusCode.Forbidden, bypass.StatusCode);

        using var acting = await switched.GetAsync("/api/pos/catalogue?outletId=5002");
        Assert.Equal(HttpStatusCode.OK, acting.StatusCode);
        using var actingDocument = JsonDocument.Parse(await acting.Content.ReadAsStringAsync());
        Assert.Equal(6, actingDocument.RootElement.GetProperty("data")[0].GetProperty("stockQty").GetInt32());
    }

    [Fact]
    public async Task SellerWithoutManagementPermissions_CanScanSearchAndPriceTheCart()
    {
        await ResetAndSeedAsync();
        using var seller = CreateClient(SellerPermissions);

        using var barcode = await seller.GetAsync("/api/pos/lookup?q=CAT-BC-RED&outletId=5001");
        Assert.Equal(HttpStatusCode.OK, barcode.StatusCode);
        using var barcodeDocument = JsonDocument.Parse(await barcode.Content.ReadAsStringAsync());
        Assert.Equal(9811, barcodeDocument.RootElement.GetProperty("data").GetProperty("variantId").GetInt64());
        Assert.Equal(7, barcodeDocument.RootElement.GetProperty("data").GetProperty("stockQty").GetInt32());

        using var sku = await seller.GetAsync("/api/pos/lookup?q=CAT-SHIRT-BLUE&outletId=5001&mode=sku");
        Assert.Equal(HttpStatusCode.OK, sku.StatusCode);

        using var inactive = await seller.GetAsync("/api/pos/lookup?q=CAT-RETIRED&outletId=5001&mode=sku");
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);

        using var foreign = await seller.GetAsync("/api/pos/lookup?q=CAT-FOREIGN&outletId=5001&mode=sku");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        using var prices = await seller.PostAsJsonAsync(
            "/api/pos/cart-prices",
            new { items = new[] { new { productVariantId = 9811, outletId = 5001, quantity = 2 } } });
        Assert.Equal(HttpStatusCode.OK, prices.StatusCode);
        using var pricesDocument = JsonDocument.Parse(await prices.Content.ReadAsStringAsync());
        Assert.Equal(
            12m,
            Assert.Single(pricesDocument.RootElement.GetProperty("data").EnumerateArray()).GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public async Task SellerStillCannotUseProductOrCategoryManagementEndpoints()
    {
        await ResetAndSeedAsync();
        using var seller = CreateClient(SellerPermissions);

        foreach (var url in new[]
                 {
                     "/api/products", "/api/products/9111", "/api/products/sku/CAT-SHIRT-RED",
                     "/api/products/barcode/CAT-BC-RED", "/api/products/category/9011",
                     "/api/products/variants/search?query=shirt", "/api/categories", "/api/categories/tree"
                 })
        {
            using var response = await seller.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{url} returned {(int)response.StatusCode}");
        }

        using var editProduct = await seller.PutAsJsonAsync("/api/products/9111", new { name = "Renamed" });
        Assert.Equal(HttpStatusCode.Forbidden, editProduct.StatusCode);
        using var deleteProduct = await seller.DeleteAsync("/api/products/9111");
        Assert.Equal(HttpStatusCode.Forbidden, deleteProduct.StatusCode);
        using var createCategory = await seller.PostAsJsonAsync("/api/categories", new { name = "New" });
        Assert.Equal(HttpStatusCode.Forbidden, createCategory.StatusCode);
    }

    [Fact]
    public async Task ProductViewerStillNeedsTheSpecificEditPermissionToChangeProducts()
    {
        await ResetAndSeedAsync();
        using var viewerAndSeller = CreateClient("products.view,categories.view,sales.create");

        using var list = await viewerAndSeller.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        using var edit = await viewerAndSeller.PutAsJsonAsync("/api/products/9111", new { name = "Renamed" });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        using var delete = await viewerAndSeller.DeleteAsync("/api/products/9111");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    private static async Task<long[]> CatalogueVariantIdsAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("variantId").GetInt64())
            .ToArray();
    }

    private HttpClient CreateClient(string permissions, bool roleSwitched = false, long? actingOutletId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "7101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Cashier");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (roleSwitched)
        {
            client.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
            client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", "Cashier");
        }
        if (actingOutletId.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Test-ActingOutletId", actingOutletId.Value.ToString());
        }

        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IPosCacheService>();
        foreach (var variantId in new long[] { 9811, 9812, 9821 })
        {
            foreach (var outletId in new long[] { 5001, 5002 })
            {
                await cache.InvalidateStockAsync(variantId, outletId);
            }
        }

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Businesses.AddRange(
            new Business { Id = 1, Name = "Catalogue business", IsActive = true },
            new Business { Id = 2, Name = "Foreign catalogue business", IsActive = true });
        db.Categories.AddRange(
            new Category { Id = 9011, BusinessId = 1, Name = "Apparel", DisplayOrder = 1 },
            new Category { Id = 9012, BusinessId = 1, Name = "Bakery", DisplayOrder = 2 },
            new Category { Id = 9013, BusinessId = 1, Name = "Retired category", DisplayOrder = 0, IsActive = false },
            new Category { Id = 9091, BusinessId = 2, Name = "Foreign category" });
        db.Outlets.AddRange(
            new Outlet { Id = 5001, BusinessId = 1, Name = "Catalogue outlet" },
            new Outlet { Id = 5002, BusinessId = 1, Name = "Other catalogue outlet" },
            new Outlet { Id = 6001, BusinessId = 2, Name = "Foreign catalogue outlet" });
        db.Users.Add(new User
        {
            Id = 7101,
            BusinessId = 1,
            Name = "Catalogue seller",
            Email = "catalogue-seller@example.com",
            PasswordHash = "x",
            OutletId = 5001,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            ContextSwitchEnabled = true,
            IsActive = true
        });
        // A non-owner's acting outlet is only honoured while it is still assigned.
        db.UserOutletAssignments.Add(new UserOutletAssignment { UserId = 7101, OutletId = 5002, BusinessId = 1 });
        db.Products.AddRange(
            new Product { Id = 9111, BusinessId = 1, Name = "Catalogue shirt", CategoryId = 9011, BasePrice = 10m, CostPrice = 4m, HasVariants = true, Status = ProductStatus.Active },
            new Product { Id = 9112, BusinessId = 1, Name = "Catalogue sourdough", CategoryId = 9012, BasePrice = 5m, CostPrice = 2m, Status = ProductStatus.Active },
            new Product { Id = 9113, BusinessId = 1, Name = "Retired product", CategoryId = 9011, BasePrice = 9m, Status = ProductStatus.Inactive },
            new Product { Id = 9191, BusinessId = 2, Name = "Foreign shirt", CategoryId = 9091, BasePrice = 99m, Status = ProductStatus.Active });
        db.ProductVariants.AddRange(
            new ProductVariant { Id = 9811, BusinessId = 1, ProductId = 9111, Name = "Red", Sku = "CAT-SHIRT-RED", Barcode = "CAT-BC-RED", PriceAdjustment = 2m },
            new ProductVariant { Id = 9812, BusinessId = 1, ProductId = 9111, Name = "Blue", Sku = "CAT-SHIRT-BLUE", Barcode = "CAT-BC-BLUE" },
            new ProductVariant { Id = 9821, BusinessId = 1, ProductId = 9112, Name = "Default", Sku = "CAT-BREAD" },
            new ProductVariant { Id = 9831, BusinessId = 1, ProductId = 9113, Name = "Default", Sku = "CAT-RETIRED" },
            new ProductVariant { Id = 9891, BusinessId = 2, ProductId = 9191, Name = "Default", Sku = "CAT-FOREIGN" });
        db.Inventories.AddRange(
            new RetailPOS.Core.Entities.Inventory { Id = 9911, VariantId = 9811, LocationId = 5001, LocationType = "outlet", Quantity = 7 },
            new RetailPOS.Core.Entities.Inventory { Id = 9912, VariantId = 9811, LocationId = 5002, LocationType = "outlet", Quantity = 3 },
            new RetailPOS.Core.Entities.Inventory { Id = 9913, VariantId = 9812, LocationId = 5002, LocationType = "outlet", Quantity = 6 },
            new RetailPOS.Core.Entities.Inventory { Id = 9914, VariantId = 9821, LocationId = 5001, LocationType = "outlet", Quantity = 20 },
            new RetailPOS.Core.Entities.Inventory { Id = 9991, VariantId = 9891, LocationId = 6001, LocationType = "outlet", Quantity = 50 });

        await db.SaveChangesAsync();
    }
}
