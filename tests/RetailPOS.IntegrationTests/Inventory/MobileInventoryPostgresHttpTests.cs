using System.Net;
using System.Text.Json;
using RetailPOS.Tests.Sales;

namespace RetailPOS.Tests.Inventory;

public sealed class MobileInventoryPostgresHttpTests
    : IClassFixture<MobileSalesDashboardPostgresFixture>
{
    private readonly MobileSalesDashboardPostgresFixture _fixture;

    public MobileInventoryPostgresHttpTests(MobileSalesDashboardPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InventoryReadsEnforceMobileRolesAndAuthorizedOutletScope()
    {
        using var owner = _fixture.CreateClient(
            4101,
            4110,
            "BusinessOwner",
            "inventory.view,low_stock_alerts.view");
        using var ownerOutletsResponse =
            await owner.GetAsync("/api/mobile/v1/inventory/outlets");
        Assert.Equal(HttpStatusCode.OK, ownerOutletsResponse.StatusCode);
        using (var outletJson = JsonDocument.Parse(await ownerOutletsResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                new long[] { 5101, 5102, 5103 },
                outletJson.RootElement.GetProperty("data").EnumerateArray()
                    .Select(outlet => outlet.GetProperty("id").GetInt64()).Order());
        }

        using var ownerStockResponse =
            await owner.GetAsync("/api/mobile/v1/inventory/outlets/5103/stock");
        Assert.Equal(HttpStatusCode.OK, ownerStockResponse.StatusCode);
        using (var ownerStockJson = JsonDocument.Parse(await ownerStockResponse.Content.ReadAsStringAsync()))
        {
            var item = Assert.Single(
                ownerStockJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            Assert.Equal(50104, item.GetProperty("inventoryId").GetInt64());
            Assert.Equal(5103, item.GetProperty("outletId").GetInt64());
            Assert.Equal(99, item.GetProperty("quantity").GetInt32());
        }

        using var manager = _fixture.CreateClient(
            4101,
            4104,
            "SalesManager",
            "inventory.view,low_stock_alerts.view");
        using var managerOutletsResponse =
            await manager.GetAsync("/api/mobile/v1/inventory/outlets");
        Assert.Equal(HttpStatusCode.OK, managerOutletsResponse.StatusCode);
        using (var managerOutletsJson =
               JsonDocument.Parse(await managerOutletsResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(
                new long[] { 5101, 5102 },
                managerOutletsJson.RootElement.GetProperty("data").EnumerateArray()
                    .Select(outlet => outlet.GetProperty("id").GetInt64()).Order());
        }

        using var managerStockResponse =
            await manager.GetAsync("/api/mobile/v1/inventory/outlets/5101/stock?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, managerStockResponse.StatusCode);
        using (var managerStockJson = JsonDocument.Parse(await managerStockResponse.Content.ReadAsStringAsync()))
        {
            var data = managerStockJson.RootElement.GetProperty("data");
            Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, data.GetProperty("pageSize").GetInt32());
            var item = Assert.Single(data.GetProperty("items").EnumerateArray());
            Assert.Equal(50101, item.GetProperty("inventoryId").GetInt64());
            Assert.Equal("MOBILE-JACKET", item.GetProperty("productCode").GetString());
            Assert.Equal("JACKET-BLUE-S", item.GetProperty("sku").GetString());
            Assert.False(item.TryGetProperty("costPrice", out _));
            Assert.False(item.TryGetProperty("retailPrice", out _));
            Assert.False(item.TryGetProperty("totalValue", out _));
        }

        using var lowStockResponse =
            await manager.GetAsync("/api/mobile/v1/inventory/outlets/5101/stock?lowStockOnly=true");
        Assert.Equal(HttpStatusCode.OK, lowStockResponse.StatusCode);
        using (var lowStockJson = JsonDocument.Parse(await lowStockResponse.Content.ReadAsStringAsync()))
        {
            var lowStockItem = Assert.Single(
                lowStockJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            Assert.Equal(50102, lowStockItem.GetProperty("inventoryId").GetInt64());
            Assert.Equal(2, lowStockItem.GetProperty("quantity").GetInt32());
            Assert.True(lowStockItem.GetProperty("isLowStock").GetBoolean());
        }

        using var noAlertPermission = _fixture.CreateClient(
            4101,
            4104,
            "Manager",
            "inventory.view");
        using var stockWithoutAlertPermission =
            await noAlertPermission.GetAsync(
                "/api/mobile/v1/inventory/outlets/5101/stock?variantId=9103");
        Assert.Equal(HttpStatusCode.OK, stockWithoutAlertPermission.StatusCode);
        using (var stockJson = JsonDocument.Parse(await stockWithoutAlertPermission.Content.ReadAsStringAsync()))
        {
            var item = Assert.Single(
                stockJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("reorderLevel").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("isLowStock").ValueKind);
        }

        using var lowStockDeniedResponse =
            await noAlertPermission.GetAsync(
                "/api/mobile/v1/inventory/outlets/5101/stock?lowStockOnly=true");
        Assert.Equal(HttpStatusCode.Forbidden, lowStockDeniedResponse.StatusCode);

        using var unauthorizedOutletResponse =
            await manager.GetAsync("/api/mobile/v1/inventory/outlets/5103/stock");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedOutletResponse.StatusCode);

        using var salesperson = _fixture.CreateClient(
            4101,
            4102,
            "SalesPerson",
            "inventory.view,low_stock_alerts.view");
        using var salespersonOutletsResponse =
            await salesperson.GetAsync("/api/mobile/v1/inventory/outlets");
        Assert.Equal(HttpStatusCode.Forbidden, salespersonOutletsResponse.StatusCode);
        using var salespersonStockResponse =
            await salesperson.GetAsync("/api/mobile/v1/inventory/outlets/5101/stock");
        Assert.Equal(HttpStatusCode.Forbidden, salespersonStockResponse.StatusCode);

        using var cashier = _fixture.CreateClient(
            4101,
            4105,
            "Cashier",
            "inventory.view,low_stock_alerts.view");
        using var cashierResponse =
            await cashier.GetAsync("/api/mobile/v1/inventory/outlets");
        Assert.Equal(HttpStatusCode.Forbidden, cashierResponse.StatusCode);

        using var missingPermission = _fixture.CreateClient(
            4101,
            4104,
            "OutletManager",
            "low_stock_alerts.view");
        using var missingPermissionResponse =
            await missingPermission.GetAsync("/api/mobile/v1/inventory/outlets");
        Assert.Equal(HttpStatusCode.Forbidden, missingPermissionResponse.StatusCode);

        using var roleSwitched = _fixture.CreateClient(
            4101,
            4110,
            "BusinessOwner",
            "inventory.view",
            true,
            "Manager");
        using var roleSwitchedResponse =
            await roleSwitched.GetAsync("/api/mobile/v1/inventory/outlets");
        Assert.Equal(HttpStatusCode.Forbidden, roleSwitchedResponse.StatusCode);

        using var wrongBusiness =
            _fixture.CreateClient(4201, 4110, "BusinessOwner", "inventory.view");
        using var wrongBusinessResponse =
            await wrongBusiness.GetAsync("/api/mobile/v1/inventory/outlets");
        Assert.Equal(HttpStatusCode.Forbidden, wrongBusinessResponse.StatusCode);
    }
}
