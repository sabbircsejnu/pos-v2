using System.Net;
using System.Text.Json;
using RetailPOS.Tests.Sales;

namespace RetailPOS.Tests.Products;

public sealed class MobileProductCataloguePostgresHttpTests
    : IClassFixture<MobileSalesDashboardPostgresFixture>
{
    private readonly MobileSalesDashboardPostgresFixture _fixture;

    public MobileProductCataloguePostgresHttpTests(MobileSalesDashboardPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CatalogueReadsAreFilteredAllowlistedAndFailClosed()
    {
        using var owner = _fixture.CreateClient(4101, 4110, "BusinessOwner", "products.view");

        using var searchResponse = await owner.GetAsync(
            "/api/mobile/v1/catalogue/products?search=jacket&categoryId=7101&status=Active"
            + "&attributeName=Colour&attributeValue=Blue&page=1&pageSize=1&businessId=4201");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        using (var searchJson = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync()))
        {
            var data = searchJson.RootElement.GetProperty("data");
            Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, data.GetProperty("page").GetInt32());
            Assert.Equal(1, data.GetProperty("pageSize").GetInt32());
            var product = Assert.Single(data.GetProperty("items").EnumerateArray());
            Assert.Equal(8102, product.GetProperty("id").GetInt64());
            Assert.Equal("MOBILE-JACKET", product.GetProperty("mainProductCode").GetString());
            Assert.Equal("Active", product.GetProperty("status").GetString());
            Assert.False(product.TryGetProperty("basePrice", out _));
            Assert.False(product.TryGetProperty("costPrice", out _));
            Assert.False(product.TryGetProperty("taxRate", out _));
            Assert.False(product.TryGetProperty("totalStock", out _));
        }

        using var defaultListResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/products?page=1&pageSize=100");
        using (var listJson = JsonDocument.Parse(await defaultListResponse.Content.ReadAsStringAsync()))
        {
            var items = listJson.RootElement.GetProperty("data").GetProperty("items");
            Assert.DoesNotContain(items.EnumerateArray(), product =>
                product.GetProperty("id").GetInt64() == 8103);
        }

        using var categoriesResponse = await owner.GetAsync("/api/mobile/v1/catalogue/categories");
        Assert.Equal(HttpStatusCode.OK, categoriesResponse.StatusCode);
        using (var categoriesJson = JsonDocument.Parse(await categoriesResponse.Content.ReadAsStringAsync()))
        {
            var category = Assert.Single(
                categoriesJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            Assert.Equal(7101, category.GetProperty("id").GetInt64());
            Assert.Equal("Dashboard category", category.GetProperty("name").GetString());
            Assert.False(category.TryGetProperty("productCount", out _));
        }

        using var productResponse = await owner.GetAsync("/api/mobile/v1/catalogue/products/8102");
        Assert.Equal(HttpStatusCode.OK, productResponse.StatusCode);
        using (var productJson = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync()))
        {
            var product = productJson.RootElement.GetProperty("data");
            Assert.Equal("Mobile Jacket", product.GetProperty("name").GetString());
            Assert.Equal("JACKET-PRODUCT-SKU", product.GetProperty("sku").GetString());
            Assert.Equal("0000123456", product.GetProperty("productBarcode").GetString());
            Assert.False(product.TryGetProperty("costPrice", out _));
        }

        using var variantsResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/products/8102/variants?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, variantsResponse.StatusCode);
        using (var variantsJson = JsonDocument.Parse(await variantsResponse.Content.ReadAsStringAsync()))
        {
            var data = variantsJson.RootElement.GetProperty("data");
            Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
            var variant = Assert.Single(data.GetProperty("items").EnumerateArray());
            Assert.Equal(9103, variant.GetProperty("id").GetInt64());
            Assert.Equal("JACKET-BLUE-S", variant.GetProperty("sku").GetString());
            Assert.Equal("Blue", variant.GetProperty("attributes").GetProperty("Colour").GetString());
            Assert.False(variant.TryGetProperty("priceAdjustment", out _));
            Assert.False(variant.TryGetProperty("costAdjustment", out _));
        }

        using var variantResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/products/8102/variants/9104");
        Assert.Equal(HttpStatusCode.OK, variantResponse.StatusCode);
        using var wrongParentVariantResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/products/8103/variants/9104");
        Assert.Equal(HttpStatusCode.NotFound, wrongParentVariantResponse.StatusCode);

        using var barcodeResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/barcodes/0000009876");
        Assert.Equal(HttpStatusCode.OK, barcodeResponse.StatusCode);
        using (var barcodeJson = JsonDocument.Parse(await barcodeResponse.Content.ReadAsStringAsync()))
        {
            var data = barcodeJson.RootElement.GetProperty("data");
            Assert.Equal("variant", data.GetProperty("matchType").GetString());
            Assert.Equal(9104, data.GetProperty("variant").GetProperty("id").GetInt64());
            Assert.Equal("0000009876", data.GetProperty("variant").GetProperty("barcode").GetString());
        }

        using var productBarcodeResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/barcodes/0000001111");
        Assert.Equal(HttpStatusCode.OK, productBarcodeResponse.StatusCode);
        using (var productBarcodeJson = JsonDocument.Parse(await productBarcodeResponse.Content.ReadAsStringAsync()))
        {
            var data = productBarcodeJson.RootElement.GetProperty("data");
            Assert.Equal("product", data.GetProperty("matchType").GetString());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("variant").ValueKind);
        }

        using var ambiguousBarcodeResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/barcodes/0000123456");
        Assert.Equal(HttpStatusCode.Conflict, ambiguousBarcodeResponse.StatusCode);
        using (var ambiguousJson = JsonDocument.Parse(await ambiguousBarcodeResponse.Content.ReadAsStringAsync()))
            Assert.Equal("AMBIGUOUS_BARCODE", ambiguousJson.RootElement.GetProperty("message").GetString());

        using var unknownBarcodeResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/barcodes/9999999999");
        Assert.Equal(HttpStatusCode.NotFound, unknownBarcodeResponse.StatusCode);

        using var invalidFilterResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/products?status=Deleted");
        Assert.Equal(HttpStatusCode.BadRequest, invalidFilterResponse.StatusCode);

        using var manager = _fixture.CreateClient(4101, 4104, "OutletManager", "products.view");
        using var managerResponse =
            await manager.GetAsync("/api/mobile/v1/catalogue/products?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, managerResponse.StatusCode);

        using var salesManager = _fixture.CreateClient(4101, 4104, "SalesManager", "products.view");
        using var salesManagerResponse =
            await salesManager.GetAsync("/api/mobile/v1/catalogue/products?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, salesManagerResponse.StatusCode);

        using var salesperson = _fixture.CreateClient(4101, 4102, "SalesPerson", "products.view");
        using var salespersonResponse =
            await salesperson.GetAsync("/api/mobile/v1/catalogue/products");
        Assert.Equal(HttpStatusCode.Forbidden, salespersonResponse.StatusCode);

        using var noPermission = _fixture.CreateClient(4101, 4110, "BusinessOwner", "");
        using var noPermissionResponse =
            await noPermission.GetAsync("/api/mobile/v1/catalogue/products");
        Assert.Equal(HttpStatusCode.Forbidden, noPermissionResponse.StatusCode);

        using var cashier = _fixture.CreateClient(4101, 4105, "Cashier", "products.view");
        using var cashierResponse =
            await cashier.GetAsync("/api/mobile/v1/catalogue/products");
        Assert.Equal(HttpStatusCode.Forbidden, cashierResponse.StatusCode);

        using var switchedOwner =
            _fixture.CreateClient(4101, 4110, "BusinessOwner", "products.view", true, "OutletManager");
        using var switchedResponse =
            await switchedOwner.GetAsync("/api/mobile/v1/catalogue/products");
        Assert.Equal(HttpStatusCode.Forbidden, switchedResponse.StatusCode);

        using var wrongBusiness = _fixture.CreateClient(4201, 4110, "BusinessOwner", "products.view");
        using var wrongBusinessResponse =
            await wrongBusiness.GetAsync("/api/mobile/v1/catalogue/products");
        Assert.Equal(HttpStatusCode.Forbidden, wrongBusinessResponse.StatusCode);

        await _fixture.AddUnownedOutletAsync();
        using var unownedOutletResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/products");
        Assert.Equal(HttpStatusCode.Forbidden, unownedOutletResponse.StatusCode);

        await _fixture.AddConflictingBusinessAsync();
        using var conflictingBusinessResponse =
            await owner.GetAsync("/api/mobile/v1/catalogue/products");
        Assert.Equal(HttpStatusCode.Forbidden, conflictingBusinessResponse.StatusCode);
    }
}
