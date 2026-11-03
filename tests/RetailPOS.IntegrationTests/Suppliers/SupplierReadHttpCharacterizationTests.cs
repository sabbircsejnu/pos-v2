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

namespace RetailPOS.IntegrationTests.Suppliers;

public sealed class SupplierReadHttpCharacterizationTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task ReadRoutes_PreserveBusinessSearchPagingPerformanceAndErrorContracts()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var listResponse = await client.GetAsync("/api/suppliers");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = await ReadDocumentAsync(listResponse);
        Assert.True(list.RootElement.GetProperty("success").GetBoolean());
        var suppliers = list.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, suppliers.Length);
        Assert.Equal(["Alpha Supplier", "Alpine Goods"],
            suppliers.Select(supplier => supplier.GetProperty("name").GetString()));

        using var searchResponse = await client.PostAsJsonAsync("/api/suppliers/search", new
        {
            searchQuery = "al",
            minCreditLimit = 500,
            maxCreditLimit = 1000,
            pageNumber = 1,
            pageSize = 1,
            sortBy = "creditLimit",
            sortOrder = "DESC"
        });
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        using var search = await ReadDocumentAsync(searchResponse);
        var searchData = search.RootElement.GetProperty("data");
        Assert.Equal(2, searchData.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, searchData.GetProperty("pageNumber").GetInt32());
        Assert.Equal(1, searchData.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, searchData.GetProperty("totalPages").GetInt32());
        Assert.True(searchData.GetProperty("hasNextPage").GetBoolean());
        Assert.False(searchData.GetProperty("hasPreviousPage").GetBoolean());
        Assert.Equal("Alpine Goods",
            Assert.Single(searchData.GetProperty("suppliers").EnumerateArray()).GetProperty("name").GetString());

        using var detailResponse = await client.GetAsync("/api/suppliers/10");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detail = await ReadDocumentAsync(detailResponse);
        Assert.Equal("Alpha Supplier", detail.RootElement.GetProperty("data").GetProperty("name").GetString());

        using var foreignResponse = await client.GetAsync("/api/suppliers/20");
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        using var foreign = await ReadDocumentAsync(foreignResponse);
        Assert.Equal("Supplier with ID 20 not found", foreign.RootElement.GetProperty("message").GetString());

        using var performanceResponse = await client.GetAsync("/api/suppliers/10/performance");
        Assert.Equal(HttpStatusCode.OK, performanceResponse.StatusCode);
        using var performance = await ReadDocumentAsync(performanceResponse);
        var metrics = performance.RootElement.GetProperty("data");
        Assert.Equal(1, metrics.GetProperty("totalPurchaseOrders").GetInt32());
        Assert.Equal(2, metrics.GetProperty("totalBills").GetInt32());
        Assert.Equal(250, metrics.GetProperty("totalPurchaseAmount").GetDecimal());
        Assert.Equal(125, metrics.GetProperty("outstandingBalance").GetDecimal());
        Assert.Equal(500, metrics.GetProperty("creditLimit").GetDecimal());
        Assert.Equal(25, metrics.GetProperty("creditUtilizationPercentage").GetDecimal());
        Assert.Equal("Good", metrics.GetProperty("healthStatus").GetString());
    }

    [Fact]
    public void SupplierReadRoutesRemainInOpenApi()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var paths = swagger.Paths
            .Where(path => path.Key.StartsWith("/api/Suppliers", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => path.Key, path => path.Value, StringComparer.OrdinalIgnoreCase);

        Assert.Contains(OperationType.Get, paths["/api/Suppliers"].Operations.Keys);
        Assert.Contains(OperationType.Post, paths["/api/Suppliers/search"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Suppliers/{id}"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Suppliers/{id}/performance"].Operations.Keys);
    }

    private HttpClient CreateClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "17");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "suppliers.view");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var supplierA = new Supplier
        {
            Id = 10,
            BusinessId = 17,
            Name = "Alpha Supplier",
            CreditLimit = 500
        };
        var supplierB = new Supplier
        {
            Id = 11,
            BusinessId = 17,
            Name = "Alpine Goods",
            CreditLimit = 1000
        };
        var foreignSupplier = new Supplier
        {
            Id = 20,
            BusinessId = 18,
            Name = "Alpha Foreign",
            CreditLimit = 2000
        };
        var warehouseA = new Warehouse
        {
            Id = 30,
            BusinessId = 17,
            Name = "A",
            Address = "Address"
        };
        var warehouseB = new Warehouse
        {
            Id = 31,
            BusinessId = 18,
            Name = "B",
            Address = "Address"
        };
        db.Suppliers.AddRange(supplierA, supplierB, foreignSupplier);
        db.Warehouses.AddRange(warehouseA, warehouseB);
        db.PurchaseOrders.AddRange(
            new PurchaseOrder
            {
                Id = 100,
                PoNumber = "PO-100",
                Supplier = supplierA,
                Warehouse = warehouseA,
                TotalAmount = 250,
                OrderDate = new DateTime(2026, 1, 1)
            },
            new PurchaseOrder
            {
                Id = 101,
                PoNumber = "PO-101",
                Supplier = supplierA,
                Warehouse = warehouseB,
                TotalAmount = 999,
                OrderDate = new DateTime(2026, 1, 2)
            });
        db.Bills.AddRange(
            new Bill
            {
                Id = 100,
                Supplier = supplierA,
                AmountDue = 125,
                DueDate = new DateTime(2026, 2, 1),
                Status = "unpaid"
            },
            new Bill
            {
                Id = 101,
                Supplier = supplierA,
                AmountDue = 77,
                DueDate = new DateTime(2026, 2, 1),
                Status = "paid"
            });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
