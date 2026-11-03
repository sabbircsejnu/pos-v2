using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.IntegrationTests.Warehouses;

public sealed class WarehouseReadHttpCharacterizationTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task ReadRoutes_PreserveBusinessScopeOrderingProjectionAndResponseShapes()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var listResponse = await client.GetAsync("/api/warehouses");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = await ReadDocumentAsync(listResponse);
        Assert.Equal("Warehouses retrieved successfully", list.RootElement.GetProperty("message").GetString());
        var warehouse = Assert.Single(list.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(10, warehouse.GetProperty("id").GetInt64());
        Assert.Equal("Alpha", warehouse.GetProperty("name").GetString());
        Assert.Equal("Main Street", warehouse.GetProperty("address").GetString());
        Assert.Equal("Manager A", warehouse.GetProperty("managerName").GetString());
        Assert.Equal(1, warehouse.GetProperty("purchaseOrderCount").GetInt32());

        using var detailResponse = await client.GetAsync("/api/warehouses/10");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detail = await ReadDocumentAsync(detailResponse);
        Assert.Equal("Warehouse retrieved successfully", detail.RootElement.GetProperty("message").GetString());
        Assert.Equal(1, detail.RootElement.GetProperty("data").GetProperty("purchaseOrderCount").GetInt32());

        using var foreignResponse = await client.GetAsync("/api/warehouses/20");
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        using var foreign = await ReadDocumentAsync(foreignResponse);
        Assert.Equal("Warehouse with ID 20 not found", foreign.RootElement.GetProperty("error").GetString());

        using var searchResponse = await client.GetAsync("/api/warehouses/search?q=Main");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        using var search = await ReadDocumentAsync(searchResponse);
        Assert.Equal("Search completed successfully", search.RootElement.GetProperty("message").GetString());
        Assert.Equal(10, Assert.Single(search.RootElement.GetProperty("data").EnumerateArray())
            .GetProperty("id").GetInt64());

        using var statsResponse = await client.GetAsync("/api/warehouses/10/stats");
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);
        using var stats = await ReadDocumentAsync(statsResponse);
        var statsData = stats.RootElement.GetProperty("data");
        Assert.Equal("Alpha", statsData.GetProperty("name").GetString());
        Assert.Equal(100, statsData.GetProperty("capacity").GetInt32());
        Assert.Equal(1, statsData.GetProperty("purchaseOrderCount").GetInt32());
        Assert.Equal(0, statsData.GetProperty("currentStock").GetInt32());
        Assert.Equal(0, statsData.GetProperty("capacityUtilization").GetDecimal());
    }

    [Fact]
    public async Task BusinessSetupWarehouseList_UsesExplicitBusinessRouteScope()
    {
        await ResetAndSeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "17");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");

        using var response = await client.GetAsync("/api/businesses/18/warehouses");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        Assert.Equal("Warehouses retrieved successfully", document.RootElement.GetProperty("message").GetString());
        var warehouse = Assert.Single(document.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("Beta", warehouse.GetProperty("name").GetString());
        Assert.Equal(0, warehouse.GetProperty("purchaseOrderCount").GetInt32());
    }

    [Fact]
    public void WarehouseReadRoutesRemainInOpenApi()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var paths = swagger.Paths
            .Where(path => path.Key.StartsWith("/api/Warehouses", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => path.Key, path => path.Value, StringComparer.OrdinalIgnoreCase);

        Assert.Contains(OperationType.Get, paths["/api/Warehouses"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Warehouses/{id}"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Warehouses/search"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Warehouses/{id}/stats"].Operations.Keys);
    }

    private HttpClient CreateClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "17");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "warehouses.view");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        var supplier = new Supplier { Id = 1, BusinessId = 17, Name = "Supplier" };
        var alpha = new Warehouse
        {
            Id = 10,
            BusinessId = 17,
            Name = "Alpha",
            Address = "Main Street",
            Capacity = 100,
            ManagerId = 1,
            Manager = CreateUser(1, 17, "Manager A")
        };
        var beta = new Warehouse
        {
            Id = 20,
            BusinessId = 18,
            Name = "Beta",
            Address = "North Road",
            Capacity = 200,
            ManagerId = 2,
            Manager = CreateUser(2, 18, "Manager B")
        };
        db.Suppliers.Add(supplier);
        db.Warehouses.AddRange(alpha, beta);
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 100,
            PoNumber = "PO-100",
            Supplier = supplier,
            Warehouse = alpha,
            SupplierId = supplier.Id,
            WarehouseId = alpha.Id,
            OrderDate = new DateTime(2026, 1, 1)
        });
        await db.SaveChangesAsync();
    }

    private static User CreateUser(long id, long businessId, string name) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            Name = name,
            Email = $"{id}@example.test",
            PasswordHash = "not-used",
            IsActive = true
        };

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
