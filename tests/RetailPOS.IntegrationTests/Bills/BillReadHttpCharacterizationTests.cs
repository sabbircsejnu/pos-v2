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

namespace RetailPOS.IntegrationTests.Bills;

public sealed class BillReadHttpCharacterizationTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task ReadRoutes_PreserveReadBehaviorAndEnforceBusinessScope()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var listResponse = await client.GetAsync("/api/bills");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = await ReadDocumentAsync(listResponse);
        var bills = list.RootElement.GetProperty("data").GetProperty("bills").EnumerateArray().ToArray();
        Assert.Equal(2, bills.Length);
        Assert.Equal(11, bills[0].GetProperty("id").GetInt64());
        Assert.False(bills[0].GetProperty("isOverdue").GetBoolean());
        Assert.Equal(10, bills[1].GetProperty("id").GetInt64());
        Assert.Equal("Local Supplier", bills[1].GetProperty("supplierName").GetString());
        Assert.True(bills[1].GetProperty("isOverdue").GetBoolean());

        using var searchResponse = await client.PostAsJsonAsync("/api/bills/search", new
        {
            status = "UNPAID",
            supplierId = 10,
            pageNumber = 1,
            pageSize = 1
        });
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        using var search = await ReadDocumentAsync(searchResponse);
        var searchData = search.RootElement.GetProperty("data");
        Assert.Equal(1, searchData.GetProperty("totalCount").GetInt32());
        Assert.Equal(10, Assert.Single(searchData.GetProperty("bills").EnumerateArray())
            .GetProperty("supplierId").GetInt64());

        using var foreignDetail = await client.GetAsync("/api/bills/12");
        Assert.Equal(HttpStatusCode.NotFound, foreignDetail.StatusCode);
        using var foreignDetailBody = await ReadDocumentAsync(foreignDetail);
        Assert.Equal("Bill with ID 12 not found",
            foreignDetailBody.RootElement.GetProperty("message").GetString());

        using var summaryResponse = await client.GetAsync("/api/bills/summary");
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        using var summary = await ReadDocumentAsync(summaryResponse);
        var summaryData = summary.RootElement.GetProperty("data");
        Assert.Equal(125, summaryData.GetProperty("totalUnpaid").GetDecimal());
        Assert.Equal(125, summaryData.GetProperty("totalOverdue").GetDecimal());
        Assert.Equal(1, summaryData.GetProperty("unpaidCount").GetInt32());
        Assert.Equal(1, summaryData.GetProperty("overdueCount").GetInt32());
    }

    [Fact]
    public void BillRoutes_PreserveReadAndBlockedWriteOpenApiContracts()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var paths = swagger.Paths
            .Where(path => path.Key.StartsWith("/api/Bills", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => path.Key, path => path.Value, StringComparer.OrdinalIgnoreCase);

        Assert.Contains(OperationType.Get, paths["/api/Bills"].Operations.Keys);
        Assert.Contains(OperationType.Post, paths["/api/Bills"].Operations.Keys);
        Assert.Contains(OperationType.Post, paths["/api/Bills/search"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Bills/{id}"].Operations.Keys);
        Assert.Contains(OperationType.Put, paths["/api/Bills/{id}/status"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Bills/summary"].Operations.Keys);
    }

    [Fact]
    public async Task LegacyBillWrites_RemainBlockedWithTheSameConflictContract()
    {
        using var client = CreateClient("accounts.view,accounts.create,accounts.edit");
        using var create = await client.PostAsJsonAsync("/api/bills", new
        {
            supplierId = 10,
            amountDue = 100,
            dueDate = "2026-12-01T00:00:00Z"
        });
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        using var createBody = await ReadDocumentAsync(create);
        Assert.Equal(
            "Legacy Bill writes are disabled. Use Supplier Payable Documents and the Journal Engine.",
            createBody.RootElement.GetProperty("message").GetString());

        using var update = await client.PutAsJsonAsync("/api/bills/10/status", new { status = "paid" });
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        using var updateBody = await ReadDocumentAsync(update);
        Assert.Equal(
            "Legacy Bill writes are disabled. Use Supplier Payable Documents and the Journal Engine.",
            updateBody.RootElement.GetProperty("message").GetString());
    }

    private HttpClient CreateClient(string permissions = "accounts.view")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "17");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var localSupplier = new Supplier { Id = 10, BusinessId = 17, Name = "Local Supplier" };
        var foreignSupplier = new Supplier { Id = 20, BusinessId = 18, Name = "Foreign Supplier" };
        db.Bills.AddRange(
            new Bill
            {
                Id = 10,
                Supplier = localSupplier,
                AmountDue = 125,
                DueDate = DateTime.UtcNow.AddDays(-2),
                Status = "unpaid"
            },
            new Bill
            {
                Id = 11,
                Supplier = localSupplier,
                AmountDue = 77,
                DueDate = DateTime.UtcNow.AddDays(2),
                Status = "paid"
            },
            new Bill
            {
                Id = 12,
                Supplier = foreignSupplier,
                AmountDue = 500,
                DueDate = DateTime.UtcNow.AddDays(-1),
                Status = "partial"
            });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
