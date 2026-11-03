using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.Warehouses;

public sealed class WarehouseMutationHttpCharacterizationTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task StandardMutationRoutes_PreserveValidationScopeAndReferentialGuards()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var createdResponse = await client.PostAsJsonAsync("/api/warehouses", new
        {
            name = "Gamma",
            address = "New Street",
            capacity = 0,
            managerId = 1
        });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.StartsWith("/api/Warehouses/", createdResponse.Headers.Location?.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
        using var created = await ReadDocumentAsync(createdResponse);
        var createdWarehouse = created.RootElement.GetProperty("data");
        var createdId = createdWarehouse.GetProperty("id").GetInt64();
        Assert.Equal("Warehouse created successfully", created.RootElement.GetProperty("message").GetString());
        Assert.Equal("Manager A", createdWarehouse.GetProperty("managerName").GetString());
        Assert.Equal(0, createdWarehouse.GetProperty("capacity").GetInt32());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(17, (await db.Warehouses.SingleAsync(item => item.Id == createdId)).BusinessId);
        }

        using var duplicateResponse = await client.PostAsJsonAsync("/api/warehouses", new
        {
            name = "Alpha",
            address = "Duplicate"
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        using var duplicate = await ReadDocumentAsync(duplicateResponse);
        Assert.Equal("A warehouse with the name 'Alpha' already exists",
            duplicate.RootElement.GetProperty("error").GetString());

        using var foreignManagerResponse = await client.PostAsJsonAsync("/api/warehouses", new
        {
            name = "Foreign Manager",
            address = "New Street",
            managerId = 2
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignManagerResponse.StatusCode);
        using var foreignManager = await ReadDocumentAsync(foreignManagerResponse);
        Assert.Equal("Manager with ID 2 not found",
            foreignManager.RootElement.GetProperty("error").GetString());

        using var negativeCapacityResponse = await client.PostAsJsonAsync("/api/warehouses", new
        {
            name = "Negative Capacity",
            address = "New Street",
            capacity = -1
        });
        Assert.Equal(HttpStatusCode.BadRequest, negativeCapacityResponse.StatusCode);
        using var negativeCapacity = await ReadDocumentAsync(negativeCapacityResponse);
        Assert.Equal("Capacity must be a positive number",
            negativeCapacity.RootElement.GetProperty("error").GetString());

        using var updateResponse = await client.PutAsJsonAsync("/api/warehouses/10", new
        {
            name = "Alpha Updated",
            address = "Updated Street",
            capacity = 150,
            managerId = 1
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updated = await ReadDocumentAsync(updateResponse);
        Assert.Equal("Alpha Updated", updated.RootElement.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("Warehouse updated successfully", updated.RootElement.GetProperty("message").GetString());

        using var guardedDeleteResponse = await client.DeleteAsync("/api/warehouses/10");
        Assert.Equal(HttpStatusCode.BadRequest, guardedDeleteResponse.StatusCode);
        using var guardedDelete = await ReadDocumentAsync(guardedDeleteResponse);
        Assert.Equal("Cannot delete warehouse. It has 1 purchase order(s)",
            guardedDelete.RootElement.GetProperty("error").GetString());

        using var missingDeleteResponse = await client.DeleteAsync("/api/warehouses/999");
        Assert.Equal(HttpStatusCode.NotFound, missingDeleteResponse.StatusCode);
        using var missingDelete = await ReadDocumentAsync(missingDeleteResponse);
        Assert.Equal("Warehouse with ID 999 not found", missingDelete.RootElement.GetProperty("error").GetString());

        using var deleteResponse = await client.DeleteAsync($"/api/warehouses/{createdId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        using var deleted = await ReadDocumentAsync(deleteResponse);
        Assert.Equal("Warehouse deleted successfully", deleted.RootElement.GetProperty("message").GetString());
    }

    private HttpClient CreateClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "17");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add(
            "X-Test-Permissions",
            "warehouses.view,warehouses.create,warehouses.edit,warehouses.delete");
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
            Capacity = 100,
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
                Capacity = 200,
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
