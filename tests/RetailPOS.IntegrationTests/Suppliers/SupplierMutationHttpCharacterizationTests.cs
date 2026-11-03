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

public sealed class SupplierMutationHttpCharacterizationTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task Mutations_PreserveBusinessValidationResponsesAndDeleteGuards()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("suppliers.view,suppliers.create,suppliers.edit,suppliers.delete");

        using var createResponse = await client.PostAsJsonAsync("/api/suppliers", new
        {
            name = "New Supplier",
            contact = "555-0123",
            address = "New address",
            creditLimit = 700
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        using var created = await ReadDocumentAsync(createResponse);
        var createdData = created.RootElement.GetProperty("data");
        var createdId = createdData.GetProperty("id").GetInt64();
        Assert.Equal("Supplier created successfully", created.RootElement.GetProperty("message").GetString());
        Assert.Equal("New Supplier", createdData.GetProperty("name").GetString());

        using var duplicateCreate = await client.PostAsJsonAsync("/api/suppliers", new
        {
            name = "alpha supplier",
            creditLimit = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateCreate.StatusCode);
        using var duplicateCreateBody = await ReadDocumentAsync(duplicateCreate);
        Assert.Equal(
            "Supplier with name 'alpha supplier' already exists",
            duplicateCreateBody.RootElement.GetProperty("message").GetString());

        using var duplicateUpdate = await client.PutAsJsonAsync("/api/suppliers/10", new
        {
            name = "New Supplier",
            creditLimit = 900
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateUpdate.StatusCode);
        using var duplicateUpdateBody = await ReadDocumentAsync(duplicateUpdate);
        Assert.Equal(
            "Supplier with name 'New Supplier' already exists",
            duplicateUpdateBody.RootElement.GetProperty("message").GetString());

        using var updateResponse = await client.PutAsJsonAsync("/api/suppliers/10", new
        {
            name = "Updated Supplier",
            contact = "555-0100",
            address = "Updated address",
            creditLimit = 1200
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updated = await ReadDocumentAsync(updateResponse);
        Assert.Equal("Supplier updated successfully", updated.RootElement.GetProperty("message").GetString());
        Assert.Equal("Updated Supplier",
            updated.RootElement.GetProperty("data").GetProperty("name").GetString());

        using var foreignUpdate = await client.PutAsJsonAsync("/api/suppliers/20", new
        {
            name = "Foreign edit",
            creditLimit = 1
        });
        Assert.Equal(HttpStatusCode.NotFound, foreignUpdate.StatusCode);
        using var foreignUpdateBody = await ReadDocumentAsync(foreignUpdate);
        Assert.Equal("Supplier with ID 20 not found",
            foreignUpdateBody.RootElement.GetProperty("message").GetString());

        using var blockedDelete = await client.DeleteAsync("/api/suppliers/10");
        Assert.Equal(HttpStatusCode.BadRequest, blockedDelete.StatusCode);
        using var blockedDeleteBody = await ReadDocumentAsync(blockedDelete);
        Assert.Equal(
            "Cannot delete supplier 'Updated Supplier'. It has 1 purchase orders and 1 bills associated with it.",
            blockedDeleteBody.RootElement.GetProperty("message").GetString());

        using var deleteResponse = await client.DeleteAsync($"/api/suppliers/{createdId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        using var deleted = await ReadDocumentAsync(deleteResponse);
        Assert.Equal("Supplier deleted successfully", deleted.RootElement.GetProperty("message").GetString());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Null(await db.Suppliers.FindAsync(createdId));
    }

    [Fact]
    public async Task MutationRoutes_PreserveAuthorizationAndOpenApiContracts()
    {
        using var unauthorizedClient = CreateClient("suppliers.view");
        using var createResponse = await unauthorizedClient.PostAsJsonAsync("/api/suppliers", new
        {
            name = "Unauthorized",
            creditLimit = 0
        });
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);

        using var editResponse = await unauthorizedClient.PutAsJsonAsync("/api/suppliers/10", new
        {
            name = "Unauthorized",
            creditLimit = 0
        });
        Assert.Equal(HttpStatusCode.Forbidden, editResponse.StatusCode);

        using var deleteResponse = await unauthorizedClient.DeleteAsync("/api/suppliers/10");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);

        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var paths = swagger.Paths
            .Where(path => path.Key.StartsWith("/api/Suppliers", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => path.Key, path => path.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(OperationType.Post, paths["/api/Suppliers"].Operations.Keys);
        Assert.Contains(OperationType.Put, paths["/api/Suppliers/{id}"].Operations.Keys);
        Assert.Contains(OperationType.Delete, paths["/api/Suppliers/{id}"].Operations.Keys);
    }

    private HttpClient CreateClient(string permissions)
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

        var supplier = new Supplier { Id = 10, BusinessId = 17, Name = "Alpha Supplier", CreditLimit = 500 };
        db.Suppliers.AddRange(
            supplier,
            new Supplier { Id = 20, BusinessId = 18, Name = "Foreign Supplier", CreditLimit = 2000 });
        db.Warehouses.Add(new Warehouse { Id = 30, BusinessId = 17, Name = "Warehouse", Address = "Address" });
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 100,
            PoNumber = "PO-100",
            Supplier = supplier,
            WarehouseId = 30,
            TotalAmount = 250,
            OrderDate = new DateTime(2026, 1, 1)
        });
        db.Bills.Add(new Bill
        {
            Id = 100,
            Supplier = supplier,
            AmountDue = 100,
            DueDate = new DateTime(2026, 2, 1),
            Status = "unpaid"
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
