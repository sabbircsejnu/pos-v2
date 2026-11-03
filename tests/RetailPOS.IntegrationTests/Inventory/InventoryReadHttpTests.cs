using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.Tests.Inventory;

public sealed class InventoryReadHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public InventoryReadHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetTotalStockByVariant_SumsOnlyTheRequestedVariantAcrossLocations()
    {
        await SeedInventoryDataAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        using var response = await client.GetAsync("/api/inventory/variant/9901/total-stock");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(9901, document.RootElement.GetProperty("data").GetProperty("variantId").GetInt64());
        Assert.Equal(20, document.RootElement.GetProperty("data").GetProperty("totalQuantity").GetInt32());
    }

    [Fact]
    public async Task GetById_PreservesInventoryProjectionAndCostRedaction()
    {
        await SeedInventoryDataAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        using var response = await client.GetAsync("/api/inventory/1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var inventory = root.GetProperty("data");
        Assert.Equal(1, inventory.GetProperty("id").GetInt64());
        Assert.Equal(9901, inventory.GetProperty("productVariantId").GetInt64());
        Assert.Equal("Inventory test product", inventory.GetProperty("productName").GetString());
        Assert.Equal(8, inventory.GetProperty("quantity").GetInt32());
        Assert.Equal(0, inventory.GetProperty("costPrice").GetDecimal());
        Assert.Equal(10, inventory.GetProperty("retailPrice").GetDecimal());
    }

    [Fact]
    public async Task GetByOutlet_ReturnsOnlyThatLocationAndRedactsCost()
    {
        await SeedInventoryDataAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        using var response = await client.GetAsync("/api/inventory/outlet/1001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var inventories = root.GetProperty("data");
        Assert.Equal(2, inventories.GetArrayLength());
        Assert.All(inventories.EnumerateArray(), inventory =>
        {
            Assert.Equal(1001, inventory.GetProperty("outletId").GetInt64());
            Assert.Equal("Test outlet", inventory.GetProperty("outletName").GetString());
            Assert.Equal(0, inventory.GetProperty("costPrice").GetDecimal());
        });
        Assert.Contains(inventories.EnumerateArray(), inventory =>
            inventory.GetProperty("id").GetInt64() == 1);
        Assert.Contains(inventories.EnumerateArray(), inventory =>
            inventory.GetProperty("id").GetInt64() == 3);
    }

    [Fact]
    public async Task GetByWarehouse_ReturnsOnlyThatLocationAndRedactsCost()
    {
        await SeedInventoryDataAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        using var response = await client.GetAsync("/api/inventory/warehouse/2001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var inventories = root.GetProperty("data");
        var inventory = Assert.Single(inventories.EnumerateArray());
        Assert.Equal(2, inventory.GetProperty("id").GetInt64());
        Assert.Equal(2001, inventory.GetProperty("warehouseId").GetInt64());
        Assert.Equal("Test warehouse", inventory.GetProperty("warehouseName").GetString());
        Assert.Equal(12, inventory.GetProperty("quantity").GetInt32());
        Assert.Equal(0, inventory.GetProperty("costPrice").GetDecimal());
    }

    [Fact]
    public async Task GetAll_PreservesAllLocationProjectionAndRedactsCost()
    {
        await SeedInventoryDataAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        using var response = await client.GetAsync("/api/inventory");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var inventories = root.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(3, inventories.Length);
        Assert.Equal(new long[] { 1, 2, 3 }, inventories.Select(item => item.GetProperty("id").GetInt64()).Order());
        Assert.All(inventories, inventory =>
            Assert.Equal(0, inventory.GetProperty("costPrice").GetDecimal()));
        Assert.Contains(inventories, inventory =>
            inventory.GetProperty("outletName").GetString() == "Test outlet");
        Assert.Contains(inventories, inventory =>
            inventory.GetProperty("warehouseName").GetString() == "Test warehouse");
    }

    [Fact]
    public async Task UpdateStockThreshold_PreservesExistingWriteContract()
    {
        await SeedInventoryDataAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.edit");

        using var response = await client.PutAsJsonAsync(
            "/api/inventory/1/threshold",
            new { reorderLevel = 17, maxStockLevel = 60 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Stock thresholds updated successfully", root.GetProperty("message").GetString());
        var inventory = root.GetProperty("data");
        Assert.Equal(1, inventory.GetProperty("id").GetInt64());
        Assert.Equal(17, inventory.GetProperty("reorderLevel").GetInt32());
        Assert.True(inventory.GetProperty("maxStockLevel").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);
        Assert.Equal(8, inventory.GetProperty("quantity").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persistedInventory = await db.Inventories.SingleAsync(item => item.Id == 1);
        Assert.Equal(17, persistedInventory.LowStockThreshold);

        using var missingResponse = await client.PutAsJsonAsync(
            "/api/inventory/999/threshold",
            new { reorderLevel = 17, maxStockLevel = 60 });
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        using var forbiddenClient = _factory.CreateClient();
        forbiddenClient.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");
        using var forbiddenResponse = await forbiddenClient.PutAsJsonAsync(
            "/api/inventory/1/threshold",
            new { reorderLevel = 18, maxStockLevel = 70 });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task GetLowStock_PreservesDefaultAndLocationFilteredResults()
    {
        await SeedInventoryDataAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "low_stock_alerts.view");

        using var defaultResponse = await client.GetAsync("/api/inventory/low-stock");
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        using var defaultDocument = JsonDocument.Parse(await defaultResponse.Content.ReadAsStringAsync());
        Assert.True(defaultDocument.RootElement.GetProperty("success").GetBoolean());
        var lowStock = Assert.Single(defaultDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(1, lowStock.GetProperty("inventoryId").GetInt64());
        Assert.Equal(8, lowStock.GetProperty("currentQuantity").GetInt32());
        Assert.Equal(10, lowStock.GetProperty("reorderLevel").GetInt32());
        Assert.Equal("Test outlet", lowStock.GetProperty("locationName").GetString());
        Assert.Equal("Outlet", lowStock.GetProperty("locationType").GetString());

        using var outletResponse = await client.GetAsync("/api/inventory/low-stock?outletId=1001");
        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        using var outletDocument = JsonDocument.Parse(await outletResponse.Content.ReadAsStringAsync());
        Assert.Single(outletDocument.RootElement.GetProperty("data").EnumerateArray());

        using var warehouseResponse = await client.GetAsync("/api/inventory/low-stock?warehouseId=2001");
        Assert.Equal(HttpStatusCode.OK, warehouseResponse.StatusCode);
        using var warehouseDocument = JsonDocument.Parse(await warehouseResponse.Content.ReadAsStringAsync());
        Assert.Empty(warehouseDocument.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task GetOutOfStock_PreservesDefaultAndLocationFilteredResults()
    {
        await SeedInventoryDataAsync(includeOutOfStock: true);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        using var defaultResponse = await client.GetAsync("/api/inventory/out-of-stock");
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        using var defaultDocument = JsonDocument.Parse(await defaultResponse.Content.ReadAsStringAsync());
        Assert.True(defaultDocument.RootElement.GetProperty("success").GetBoolean());
        var inventory = Assert.Single(defaultDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(4, inventory.GetProperty("id").GetInt64());
        Assert.Equal(0, inventory.GetProperty("quantity").GetInt32());
        Assert.Equal("Test outlet", inventory.GetProperty("outletName").GetString());
        Assert.Equal(0, inventory.GetProperty("costPrice").GetDecimal());

        using var outletResponse = await client.GetAsync("/api/inventory/out-of-stock?outletId=1001");
        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        using var outletDocument = JsonDocument.Parse(await outletResponse.Content.ReadAsStringAsync());
        Assert.Single(outletDocument.RootElement.GetProperty("data").EnumerateArray());

        using var warehouseResponse = await client.GetAsync("/api/inventory/out-of-stock?warehouseId=2001");
        Assert.Equal(HttpStatusCode.OK, warehouseResponse.StatusCode);
        using var warehouseDocument = JsonDocument.Parse(await warehouseResponse.Content.ReadAsStringAsync());
        Assert.Empty(warehouseDocument.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task GetExpiringSoon_PreservesDaysAndLocationFilters()
    {
        await SeedInventoryDataAsync(includeExpiringSoon: true);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        using var defaultResponse = await client.GetAsync("/api/inventory/expiring-soon?days=30");
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        using var defaultDocument = JsonDocument.Parse(await defaultResponse.Content.ReadAsStringAsync());
        var inventory = Assert.Single(defaultDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(5, inventory.GetProperty("id").GetInt64());
        Assert.Equal(2001, inventory.GetProperty("warehouseId").GetInt64());
        Assert.Equal("Test warehouse", inventory.GetProperty("warehouseName").GetString());
        Assert.Equal(0, inventory.GetProperty("costPrice").GetDecimal());

        using var outletResponse = await client.GetAsync("/api/inventory/expiring-soon?days=30&outletId=1001");
        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        using var outletDocument = JsonDocument.Parse(await outletResponse.Content.ReadAsStringAsync());
        Assert.Empty(outletDocument.RootElement.GetProperty("data").EnumerateArray());

        using var warehouseResponse = await client.GetAsync("/api/inventory/expiring-soon?days=30&warehouseId=2001");
        Assert.Equal(HttpStatusCode.OK, warehouseResponse.StatusCode);
        using var warehouseDocument = JsonDocument.Parse(await warehouseResponse.Content.ReadAsStringAsync());
        Assert.Single(warehouseDocument.RootElement.GetProperty("data").EnumerateArray());

        using var shorterWindowResponse = await client.GetAsync("/api/inventory/expiring-soon?days=10&warehouseId=2001");
        Assert.Equal(HttpStatusCode.OK, shorterWindowResponse.StatusCode);
        using var shorterWindowDocument = JsonDocument.Parse(await shorterWindowResponse.Content.ReadAsStringAsync());
        Assert.Empty(shorterWindowDocument.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task GetInventorySummary_PreservesGroupedTotalsOrderingAndValueRedaction()
    {
        await SeedInventoryDataAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        using var response = await client.GetAsync("/api/inventory/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Inventory summary for 2 locations", root.GetProperty("message").GetString());
        var summaries = root.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, summaries.Length);

        Assert.Equal("Outlet", summaries[0].GetProperty("locationType").GetString());
        Assert.Equal(1001, summaries[0].GetProperty("outletId").GetInt64());
        Assert.Equal("Test outlet", summaries[0].GetProperty("outletName").GetString());
        Assert.Equal(2, summaries[0].GetProperty("totalProducts").GetInt32());
        Assert.Equal(108, summaries[0].GetProperty("totalQuantity").GetInt32());
        Assert.Equal(1, summaries[0].GetProperty("lowStockCount").GetInt32());
        Assert.Equal(0, summaries[0].GetProperty("totalValue").GetDecimal());

        Assert.Equal("Warehouse", summaries[1].GetProperty("locationType").GetString());
        Assert.Equal(2001, summaries[1].GetProperty("warehouseId").GetInt64());
        Assert.Equal("Test warehouse", summaries[1].GetProperty("warehouseName").GetString());
        Assert.Equal(1, summaries[1].GetProperty("totalProducts").GetInt32());
        Assert.Equal(12, summaries[1].GetProperty("totalQuantity").GetInt32());
        Assert.Equal(0, summaries[1].GetProperty("totalValue").GetDecimal());
    }

    [Fact]
    public async Task GetValuation_PreservesAllAndLocationFilteredTotalsAndCostRedaction()
    {
        await SeedInventoryDataAsync();

        using var restrictedClient = _factory.CreateClient();
        restrictedClient.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        using var allResponse = await restrictedClient.GetAsync("/api/inventory/valuation");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        using var allDocument = JsonDocument.Parse(await allResponse.Content.ReadAsStringAsync());
        var allRoot = allDocument.RootElement;
        Assert.True(allRoot.GetProperty("success").GetBoolean());
        Assert.Equal("Inventory valuation calculated successfully", allRoot.GetProperty("message").GetString());
        var allValuation = allRoot.GetProperty("data");
        Assert.Equal("All Locations", allValuation.GetProperty("locationName").GetString());
        Assert.Equal("All", allValuation.GetProperty("locationType").GetString());
        Assert.Equal(3, allValuation.GetProperty("totalItems").GetInt32());
        Assert.Equal(120, allValuation.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(0, allValuation.GetProperty("totalCostValue").GetDecimal());
        Assert.Equal(1200, allValuation.GetProperty("totalRetailValue").GetDecimal());
        var restrictedCategory = Assert.Single(allValuation.GetProperty("categoryBreakdown").EnumerateArray());
        Assert.Equal("Inventory test", restrictedCategory.GetProperty("categoryName").GetString());
        Assert.Equal(2, restrictedCategory.GetProperty("productCount").GetInt32());
        Assert.Equal(120, restrictedCategory.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(0, restrictedCategory.GetProperty("totalCostValue").GetDecimal());
        Assert.Equal(1200, restrictedCategory.GetProperty("totalRetailValue").GetDecimal());

        using var outletResponse = await restrictedClient.GetAsync("/api/inventory/valuation?outletId=1001");
        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        using var outletDocument = JsonDocument.Parse(await outletResponse.Content.ReadAsStringAsync());
        var outletValuation = outletDocument.RootElement.GetProperty("data");
        Assert.Equal("Test outlet", outletValuation.GetProperty("locationName").GetString());
        Assert.Equal("Outlet", outletValuation.GetProperty("locationType").GetString());
        Assert.Equal(2, outletValuation.GetProperty("totalItems").GetInt32());
        Assert.Equal(108, outletValuation.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(0, outletValuation.GetProperty("totalCostValue").GetDecimal());

        using var costClient = _factory.CreateClient();
        costClient.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view,products.view_cost");
        using var costResponse = await costClient.GetAsync("/api/inventory/valuation?warehouseId=2001");
        Assert.Equal(HttpStatusCode.OK, costResponse.StatusCode);
        using var costDocument = JsonDocument.Parse(await costResponse.Content.ReadAsStringAsync());
        var warehouseValuation = costDocument.RootElement.GetProperty("data");
        Assert.Equal("Test warehouse", warehouseValuation.GetProperty("locationName").GetString());
        Assert.Equal("Warehouse", warehouseValuation.GetProperty("locationType").GetString());
        Assert.Equal(1, warehouseValuation.GetProperty("totalItems").GetInt32());
        Assert.Equal(12, warehouseValuation.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(60, warehouseValuation.GetProperty("totalCostValue").GetDecimal());
        Assert.Equal(120, warehouseValuation.GetProperty("totalRetailValue").GetDecimal());
        var warehouseCategory = Assert.Single(warehouseValuation.GetProperty("categoryBreakdown").EnumerateArray());
        Assert.Equal(60, warehouseCategory.GetProperty("totalCostValue").GetDecimal());
        Assert.Equal(120, warehouseCategory.GetProperty("totalRetailValue").GetDecimal());
    }

    [Fact]
    public void ValuationAndVariantFilterRoutesRemainInOpenApi()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var path = swagger.Paths.Single(item =>
            string.Equals(item.Key, "/api/Inventory/valuation", StringComparison.OrdinalIgnoreCase));
        var operation = path.Value.Operations[OperationType.Get];

        Assert.Contains(operation.Parameters, parameter => parameter.Name == "outletId");
        Assert.Contains(operation.Parameters, parameter => parameter.Name == "warehouseId");

        var filterPath = swagger.Paths.Single(item =>
            string.Equals(item.Key, "/api/Inventory/variant-attribute-filters", StringComparison.OrdinalIgnoreCase));
        var filterOperation = filterPath.Value.Operations[OperationType.Get];
        Assert.Contains(filterOperation.Parameters, parameter => parameter.Name == "outletId");
        Assert.Contains(filterOperation.Parameters, parameter => parameter.Name == "warehouseId");

        var searchPath = swagger.Paths.Single(item =>
            string.Equals(item.Key, "/api/Inventory/search", StringComparison.OrdinalIgnoreCase));
        var searchOperation = searchPath.Value.Operations[OperationType.Post];
        Assert.NotNull(searchOperation.RequestBody);
    }

    [Fact]
    public async Task GetVariantAttributeFilters_ReturnsOnlyUsedActiveValuesInSetupOrderAndScopesByLocation()
    {
        await SeedInventoryDataAsync(includeVariantAttributeFilters: true);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        using var allResponse = await client.GetAsync("/api/inventory/variant-attribute-filters");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        using var allDocument = JsonDocument.Parse(await allResponse.Content.ReadAsStringAsync());
        Assert.True(allDocument.RootElement.GetProperty("success").GetBoolean());
        var allFilters = allDocument.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Size", "Color" }, allFilters
            .Select(filter => filter.GetProperty("attributeName").GetString()));
        Assert.Equal(new[] { "S", "M" }, allFilters[0].GetProperty("values")
            .EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(new[] { "Red", "Blue" }, allFilters[1].GetProperty("values")
            .EnumerateArray().Select(value => value.GetString()));

        using var outletResponse = await client.GetAsync(
            "/api/inventory/variant-attribute-filters?outletId=1001");
        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        using var outletDocument = JsonDocument.Parse(await outletResponse.Content.ReadAsStringAsync());
        var outletFilters = outletDocument.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(new[] { "S", "M" }, outletFilters[0].GetProperty("values")
            .EnumerateArray().Select(value => value.GetString()));

        using var warehouseResponse = await client.GetAsync(
            "/api/inventory/variant-attribute-filters?warehouseId=2001");
        Assert.Equal(HttpStatusCode.OK, warehouseResponse.StatusCode);
        using var warehouseDocument = JsonDocument.Parse(await warehouseResponse.Content.ReadAsStringAsync());
        var warehouseFilters = warehouseDocument.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Size", "Color" }, warehouseFilters
            .Select(filter => filter.GetProperty("attributeName").GetString()));
        Assert.Equal(new[] { "M" }, warehouseFilters[0].GetProperty("values")
            .EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(new[] { "Red", "Blue" }, warehouseFilters[1].GetProperty("values")
            .EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task Search_PreservesLocationFiltersAttributeLogicPaginationExpiryAndCostRedaction()
    {
        await SeedInventoryDataAsync(includeExpiringSoon: true, includeVariantAttributeFilters: true);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "inventory.view");

        var firstPageRequest = new
        {
            categoryId = 7001,
            outletId = 1001,
            page = 1,
            pageSize = 1
        };
        using var firstPageResponse = await client.PostAsJsonAsync(
            "/api/inventory/search",
            firstPageRequest);
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        using var firstPageDocument = JsonDocument.Parse(await firstPageResponse.Content.ReadAsStringAsync());
        var firstPageRoot = firstPageDocument.RootElement;
        Assert.True(firstPageRoot.GetProperty("success").GetBoolean());
        Assert.Equal("Found 1 matching inventory records", firstPageRoot.GetProperty("message").GetString());
        var firstPageInventory = Assert.Single(firstPageRoot.GetProperty("data").EnumerateArray());
        Assert.Equal(1, firstPageInventory.GetProperty("id").GetInt64());
        Assert.Equal(1001, firstPageInventory.GetProperty("outletId").GetInt64());
        Assert.Equal(0, firstPageInventory.GetProperty("costPrice").GetDecimal());

        using var secondPageResponse = await client.PostAsJsonAsync(
            "/api/inventory/search",
            new
            {
                categoryId = 7001,
                outletId = 1001,
                page = 2,
                pageSize = 1
            });
        Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);
        using var secondPageDocument = JsonDocument.Parse(await secondPageResponse.Content.ReadAsStringAsync());
        var secondPageInventory = Assert.Single(secondPageDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(3, secondPageInventory.GetProperty("id").GetInt64());

        using var attributesResponse = await client.PostAsJsonAsync(
            "/api/inventory/search",
            new
            {
                categoryId = 7001,
                outletId = 1001,
                lowStockOnly = true,
                variantAttributeFilters = new[]
                {
                    new { attributeName = "Size", values = new[] { "M", "S" } },
                    new { attributeName = "Color", values = new[] { "Red", "Blue" } }
                }
            });
        Assert.Equal(HttpStatusCode.OK, attributesResponse.StatusCode);
        using var attributesDocument = JsonDocument.Parse(await attributesResponse.Content.ReadAsStringAsync());
        var attributeInventory = Assert.Single(attributesDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(1, attributeInventory.GetProperty("id").GetInt64());
        Assert.Equal(8, attributeInventory.GetProperty("quantity").GetInt32());

        using var expiringResponse = await client.PostAsJsonAsync(
            "/api/inventory/search",
            new
            {
                warehouseId = 2001,
                expiringSoon = true,
                expiringWithinDays = 30
            });
        Assert.Equal(HttpStatusCode.OK, expiringResponse.StatusCode);
        using var expiringDocument = JsonDocument.Parse(await expiringResponse.Content.ReadAsStringAsync());
        var expiringInventory = Assert.Single(expiringDocument.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(5, expiringInventory.GetProperty("id").GetInt64());
        Assert.Equal(0, expiringInventory.GetProperty("costPrice").GetDecimal());
    }

    private async Task SeedInventoryDataAsync(
        bool includeOutOfStock = false,
        bool includeExpiringSoon = false,
        bool includeVariantAttributeFilters = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Inventory test business" });
        db.Users.Add(new User
        {
            Id = 1,
            BusinessId = 1,
            OutletId = 1001,
            Name = "Inventory test user",
            Email = "inventory-test@example.com",
            PasswordHash = "x",
            InventoryLocationAccessScope = User.InventoryAccessAll,
            IsActive = true
        });
        db.Categories.Add(new Category { Id = 7001, Name = "Inventory test", BusinessId = 1 });
        db.Products.Add(new Product
        {
            Id = 9001,
            BusinessId = 1,
            Name = "Inventory test product",
            ProductCode = "INV-001",
            CategoryId = 7001,
            BasePrice = 10m,
            CostPrice = 5m
        });
        db.Outlets.Add(new Outlet { Id = 1001, BusinessId = 1, Name = "Test outlet" });
        db.Warehouses.Add(new Warehouse { Id = 2001, BusinessId = 1, Name = "Test warehouse" });
        db.ProductVariants.AddRange(
            new ProductVariant
            {
                Id = 9901,
                BusinessId = 1,
                ProductId = 9001,
                Name = "First",
                Sku = "INV-001-A",
                Attributes = includeVariantAttributeFilters
                    ? """{"Size":"M","Color":["Blue","Red"],"Fabric":"Cotton"}"""
                    : "{}"
            },
            new ProductVariant
            {
                Id = 9902,
                BusinessId = 1,
                ProductId = 9001,
                Name = "Second",
                Sku = "INV-001-B",
                Attributes = includeVariantAttributeFilters
                    ? """{"size":"S"}"""
                    : "{}"
            });
        if (includeVariantAttributeFilters)
        {
            db.Variations.AddRange(
                new Variation { Id = 7101, Name = "Size", DisplayOrder = 1, IsActive = true },
                new Variation { Id = 7102, Name = "Color", DisplayOrder = 2, IsActive = true },
                new Variation { Id = 7103, Name = "Fabric", DisplayOrder = 3, IsActive = false },
                new Variation { Id = 7104, Name = "Pattern", DisplayOrder = 4, IsActive = true });
            db.VariationOptions.AddRange(
                new VariationOption { Id = 7201, VariationId = 7101, Name = "S", DisplayOrder = 0, IsActive = true },
                new VariationOption { Id = 7202, VariationId = 7101, Name = "M", DisplayOrder = 1, IsActive = true },
                new VariationOption { Id = 7203, VariationId = 7101, Name = "L", DisplayOrder = 2, IsActive = false },
                new VariationOption { Id = 7211, VariationId = 7102, Name = "Red", DisplayOrder = 0, IsActive = true },
                new VariationOption { Id = 7212, VariationId = 7102, Name = "Blue", DisplayOrder = 1, IsActive = true },
                new VariationOption { Id = 7213, VariationId = 7102, Name = "Black", DisplayOrder = 2, IsActive = false },
                new VariationOption { Id = 7221, VariationId = 7103, Name = "Cotton", DisplayOrder = 0, IsActive = true },
                new VariationOption { Id = 7231, VariationId = 7104, Name = "Striped", DisplayOrder = 0, IsActive = true });
        }
        db.Inventories.AddRange(
            new RetailPOS.Core.Entities.Inventory { Id = 1, VariantId = 9901, LocationId = 1001, LocationType = "outlet", Quantity = 8 },
            new RetailPOS.Core.Entities.Inventory { Id = 2, VariantId = 9901, LocationId = 2001, LocationType = "warehouse", Quantity = 12 },
            new RetailPOS.Core.Entities.Inventory { Id = 3, VariantId = 9902, LocationId = 1001, LocationType = "outlet", Quantity = 100 });
        if (includeOutOfStock)
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = 4,
                VariantId = 9901,
                LocationId = 1001,
                LocationType = "outlet",
                Quantity = 0
            });
        if (includeExpiringSoon)
            db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
            {
                Id = 5,
                VariantId = 9901,
                LocationId = 2001,
                LocationType = "warehouse",
                Quantity = 6,
                ExpiryDate = DateTime.UtcNow.AddDays(15)
            });
        await db.SaveChangesAsync();
    }
}
