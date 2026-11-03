using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.IntegrationTests.Warehouses;

public sealed class BusinessSetupWarehouseWriteHttpCharacterizationTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task BusinessSetupWarehouseWrites_PreserveScopeValidationAndAuditOutcomes()
    {
        await ResetAndSeedAsync();
        using var client = CreateSuperAdminClient();

        using var createResponse = await client.PostAsJsonAsync("/api/businesses/17/warehouses", new
        {
            name = "Created",
            address = "New Street",
            capacity = 0,
            managerId = 1
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal("/api/businesses/17/warehouses", createResponse.Headers.Location?.AbsolutePath);
        using var created = await ReadDocumentAsync(createResponse);
        Assert.True(created.RootElement.GetProperty("success").GetBoolean());
        var createdWarehouse = created.RootElement.GetProperty("data");
        var createdId = createdWarehouse.GetProperty("id").GetInt64();
        Assert.Equal("Created", createdWarehouse.GetProperty("name").GetString());
        Assert.Equal("Manager A", createdWarehouse.GetProperty("managerName").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(17, (await db.Warehouses.SingleAsync(item => item.Id == createdId)).BusinessId);
            var audit = await db.AuditEvents.Include(value => value.Entities)
                .SingleAsync(value => value.ActionSummary == "Warehouse 'Created' created for business 17");
            Assert.Equal(AuditActionType.Create, audit.ActionType);
            Assert.Equal("Warehouse", audit.PrimaryEntityType);
            Assert.Equal(createdId.ToString(), audit.PrimaryEntityId);
            Assert.Equal("{\"businessId\":17}", Assert.Single(audit.Entities).Metadata);
        }

        using var duplicateResponse = await client.PostAsJsonAsync("/api/businesses/17/warehouses", new
        {
            name = "Alpha",
            address = "Duplicate"
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        using var duplicate = await ReadDocumentAsync(duplicateResponse);
        Assert.Equal("A warehouse with the name 'Alpha' already exists in this business",
            duplicate.RootElement.GetProperty("message").GetString());

        using var foreignManagerResponse = await client.PostAsJsonAsync("/api/businesses/17/warehouses", new
        {
            name = "Foreign Manager",
            address = "New Street",
            managerId = 2
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignManagerResponse.StatusCode);
        using var foreignManager = await ReadDocumentAsync(foreignManagerResponse);
        Assert.Equal("Manager with ID 2 not found in this business",
            foreignManager.RootElement.GetProperty("message").GetString());

        using var updateResponse = await client.PutAsJsonAsync("/api/businesses/17/warehouses/10", new
        {
            name = "Alpha Updated",
            address = "Updated Street",
            capacity = 150,
            managerId = 1
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updated = await ReadDocumentAsync(updateResponse);
        Assert.True(updated.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Alpha Updated", updated.RootElement.GetProperty("data").GetProperty("name").GetString());

        using var foreignUpdateResponse = await client.PutAsJsonAsync("/api/businesses/17/warehouses/20", new
        {
            name = "Foreign",
            address = "No Access"
        });
        Assert.Equal(HttpStatusCode.NotFound, foreignUpdateResponse.StatusCode);
        using var foreignUpdate = await ReadDocumentAsync(foreignUpdateResponse);
        Assert.Equal("Warehouse 20 not found in business 17",
            foreignUpdate.RootElement.GetProperty("message").GetString());

        using var guardedDeleteResponse = await client.DeleteAsync("/api/businesses/17/warehouses/10");
        Assert.Equal(HttpStatusCode.BadRequest, guardedDeleteResponse.StatusCode);
        using var guardedDelete = await ReadDocumentAsync(guardedDeleteResponse);
        Assert.Equal("Cannot delete warehouse. It has 1 purchase order(s)",
            guardedDelete.RootElement.GetProperty("message").GetString());

        using var missingDeleteResponse = await client.DeleteAsync("/api/businesses/17/warehouses/20");
        Assert.Equal(HttpStatusCode.NotFound, missingDeleteResponse.StatusCode);
        using var missingDelete = await ReadDocumentAsync(missingDeleteResponse);
        Assert.Equal("Warehouse 20 not found in business 17",
            missingDelete.RootElement.GetProperty("message").GetString());

        using var deleteResponse = await client.DeleteAsync($"/api/businesses/17/warehouses/{createdId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        using var deleted = await ReadDocumentAsync(deleteResponse);
        Assert.True(deleted.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Warehouse deleted successfully", deleted.RootElement.GetProperty("message").GetString());

        using var verification = factory.Services.CreateScope();
        var verifiedDb = verification.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.DoesNotContain(await verifiedDb.Warehouses.ToListAsync(), item => item.Id == createdId);
        Assert.Equal(1, await verifiedDb.AuditEvents.CountAsync(value =>
            value.ActionType == AuditActionType.Update
            && value.ActionSummary == "Warehouse 'Alpha Updated' updated for business 17"));
        Assert.Equal(1, await verifiedDb.AuditEvents.CountAsync(value =>
            value.ActionType == AuditActionType.Delete
            && value.ActionSummary == $"Warehouse {createdId} deleted from business 17"));
    }

    [Fact]
    public void BusinessSetupWarehouseMutationRoutesRemainInOpenApi()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var path = swagger.Paths["/api/businesses/{businessId}/warehouses"];
        Assert.Contains(OperationType.Get, path.Operations.Keys);
        Assert.Contains(OperationType.Post, path.Operations.Keys);
        Assert.Contains(OperationType.Put, swagger.Paths["/api/businesses/{businessId}/warehouses/{id}"].Operations.Keys);
        Assert.Contains(OperationType.Delete, swagger.Paths["/api/businesses/{businessId}/warehouses/{id}"].Operations.Keys);
    }

    private HttpClient CreateSuperAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "17");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        var supplier = new Supplier { Id = 1, BusinessId = 17, Name = "Supplier" };
        var warehouse = new Warehouse
        {
            Id = 10,
            BusinessId = 17,
            Name = "Alpha",
            Address = "Main Street",
            ManagerId = 1
        };
        db.Suppliers.Add(supplier);
        db.Users.AddRange(
            CreateUser(1, 17, "Manager A"),
            CreateUser(2, 18, "Manager B"));
        db.Warehouses.AddRange(
            warehouse,
            new Warehouse
            {
                Id = 20,
                BusinessId = 18,
                Name = "Beta",
                Address = "North Road",
                ManagerId = 2
            });
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 100,
            PoNumber = "PO-100",
            Supplier = supplier,
            Warehouse = warehouse,
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
