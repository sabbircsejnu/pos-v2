using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RetailPOS.Tests.Inventory;

namespace RetailPOS.Tests.Products;

[Trait("Category", "LiveInfrastructure")]
public sealed class ProductStockHttpTests(InventoryLocationPostgresFixture fixture)
    : IClassFixture<InventoryLocationPostgresFixture>
{
    [Theory]
    [InlineData(null, null, 5)]
    [InlineData(5002L, null, 2)]
    [InlineData(null, 2001L, 3)]
    public async Task ListAndDetail_UseAuthorizedInventoryAndExposeSameStatus(long? outletId, long? warehouseId, int expectedQuantity)
    {
        using var client = fixture.CreateClient(permissions: "products.view");
        fixture.Sql.Clear();
        using var listResponse = await client.PostAsJsonAsync("/api/products/search", new { outletId, warehouseId });
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Single(fixture.Sql.InventoryQueries());
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var product = Assert.Single(list.RootElement.GetProperty("data").GetProperty("products").EnumerateArray());
        Assert.Equal("partially_in_stock", product.GetProperty("stockStatus").GetString());
        Assert.Equal(expectedQuantity, product.GetProperty("totalStock").GetInt32());
        Assert.Equal(JsonValueKind.Null, product.GetProperty("costPrice").ValueKind);

        var query = outletId.HasValue ? $"?outletId={outletId}"
            : warehouseId.HasValue ? $"?warehouseId={warehouseId}" : "";
        fixture.Sql.Clear();
        using var detailResponse = await client.GetAsync($"/api/products/8001{query}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Single(fixture.Sql.InventoryQueries());
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        var item = detail.RootElement.GetProperty("data");
        Assert.Equal(product.GetProperty("stockStatus").GetString(), item.GetProperty("stockStatus").GetString());
        Assert.Equal(expectedQuantity, item.GetProperty("totalStock").GetInt32());
    }

    [Theory]
    [InlineData(5003L, null, HttpStatusCode.Forbidden)]
    [InlineData(6001L, null, HttpStatusCode.Forbidden)]
    [InlineData(null, 2002L, HttpStatusCode.Forbidden)]
    [InlineData(null, 6002L, HttpStatusCode.Forbidden)]
    [InlineData(5001L, 2001L, HttpStatusCode.BadRequest)]
    [InlineData(-1L, null, HttpStatusCode.BadRequest)]
    public async Task ExplicitInvalidOrUnauthorizedLocations_AreRejectedBeforeInventoryReads(
        long? outletId, long? warehouseId, HttpStatusCode expectedStatus)
    {
        using var client = fixture.CreateClient(permissions: "products.view");
        fixture.Sql.Clear();
        using var list = await client.PostAsJsonAsync("/api/products/search", new { outletId, warehouseId });
        Assert.Equal(expectedStatus, list.StatusCode);
        var query = $"?{(outletId.HasValue ? $"outletId={outletId}&" : "")}{(warehouseId.HasValue ? $"warehouseId={warehouseId}" : "")}";
        using var detail = await client.GetAsync($"/api/products/8001{query}");
        Assert.Equal(expectedStatus, detail.StatusCode);
        Assert.Empty(fixture.Sql.InventoryQueries());
    }
}
