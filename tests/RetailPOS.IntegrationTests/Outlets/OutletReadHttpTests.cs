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

namespace RetailPOS.IntegrationTests.Outlets;

public sealed class OutletReadHttpTests(RetailPosApiFactory factory)
    : IClassFixture<RetailPosApiFactory>
{
    [Fact]
    public async Task ReadRoutes_PreserveTenantScopeSearchStatsAndResponseShapes()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient();

        using var listResponse = await client.GetAsync("/api/outlets");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = await ReadDocumentAsync(listResponse);
        Assert.Equal("Outlets retrieved successfully", list.RootElement.GetProperty("message").GetString());
        var outlet = Assert.Single(list.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(10, outlet.GetProperty("id").GetInt64());
        Assert.Equal("Alpha", outlet.GetProperty("name").GetString());
        Assert.Equal("Manager A", outlet.GetProperty("managerName").GetString());
        Assert.Equal(2, outlet.GetProperty("userCount").GetInt32());

        using var detailResponse = await client.GetAsync("/api/outlets/10");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detail = await ReadDocumentAsync(detailResponse);
        Assert.Equal("Outlet retrieved successfully", detail.RootElement.GetProperty("message").GetString());
        Assert.Equal(10, detail.RootElement.GetProperty("data").GetProperty("id").GetInt64());

        using var missingResponse = await client.GetAsync("/api/outlets/20");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var missing = await ReadDocumentAsync(missingResponse);
        Assert.Equal("Outlet with ID 20 not found", missing.RootElement.GetProperty("error").GetString());

        using var searchResponse = await client.GetAsync("/api/outlets/search?q=555-0100");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        using var search = await ReadDocumentAsync(searchResponse);
        Assert.Equal("Search completed successfully", search.RootElement.GetProperty("message").GetString());
        Assert.Equal(10, Assert.Single(search.RootElement.GetProperty("data").EnumerateArray())
            .GetProperty("id").GetInt64());

        using var statsResponse = await client.GetAsync("/api/outlets/10/stats");
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);
        using var stats = await ReadDocumentAsync(statsResponse);
        var statsData = stats.RootElement.GetProperty("data");
        Assert.Equal(2, statsData.GetProperty("userCount").GetInt32());
        Assert.Equal(0, statsData.GetProperty("salesCount").GetInt32());
        Assert.Equal(0, statsData.GetProperty("totalSales").GetDecimal());
    }

    [Fact]
    public async Task BusinessSetupOutletList_UsesExplicitBusinessRouteScope()
    {
        await ResetAndSeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "17");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");

        using var response = await client.GetAsync("/api/businesses/18/outlets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        Assert.Equal("Outlets retrieved successfully",
            document.RootElement.GetProperty("message").GetString());
        var outlet = Assert.Single(document.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("Beta", outlet.GetProperty("name").GetString());
        Assert.Equal(2, outlet.GetProperty("userCount").GetInt32());
    }

    [Fact]
    public void OutletReadRoutesRemainInOpenApi()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var paths = swagger.Paths
            .Where(path => path.Key.StartsWith("/api/Outlets", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => path.Key, path => path.Value, StringComparer.OrdinalIgnoreCase);

        Assert.Contains(OperationType.Get, paths["/api/Outlets"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Outlets/{id}"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Outlets/search"].Operations.Keys);
        Assert.Contains(OperationType.Get, paths["/api/Outlets/{id}/stats"].Operations.Keys);
    }

    [Fact]
    public async Task LegacyWriteRoutes_CharacterizeValidationScopeAndActiveUserDeleteGate()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("outlets.view,outlets.create,outlets.edit,outlets.delete");

        using var missingName = await client.PostAsJsonAsync("/api/outlets", new
        {
            name = " ",
            address = "Address"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);
        using var missingNameBody = await ReadDocumentAsync(missingName);
        Assert.Equal("Outlet name is required", missingNameBody.RootElement.GetProperty("error").GetString());

        using var create = await client.PostAsJsonAsync("/api/outlets", new
        {
            name = "Gamma",
            address = "New Street",
            contactNumber = "555-0300",
            managerId = 1
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = await ReadDocumentAsync(create);
        var createdOutlet = created.RootElement.GetProperty("data");
        Assert.Equal("Outlet created successfully", created.RootElement.GetProperty("message").GetString());
        Assert.Equal("Gamma", createdOutlet.GetProperty("name").GetString());
        Assert.Equal("Manager A", createdOutlet.GetProperty("managerName").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(17, (await db.Outlets.SingleAsync(item => item.Name == "Gamma")).BusinessId);
        }

        using var duplicate = await client.PostAsJsonAsync("/api/outlets", new
        {
            name = "Gamma",
            address = "Another Street"
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var duplicateBody = await ReadDocumentAsync(duplicate);
        Assert.Equal("An outlet with the name 'Gamma' already exists",
            duplicateBody.RootElement.GetProperty("error").GetString());

        using var foreignManager = await client.PostAsJsonAsync("/api/outlets", new
        {
            name = "Invalid manager",
            address = "Another Street",
            managerId = 2
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignManager.StatusCode);
        using var foreignManagerBody = await ReadDocumentAsync(foreignManager);
        Assert.Equal("Manager with ID 2 not found", foreignManagerBody.RootElement.GetProperty("error").GetString());

        using var update = await client.PutAsJsonAsync("/api/outlets/10", new
        {
            name = "Alpha Updated",
            address = "Updated Street",
            contactNumber = "555-0199",
            managerId = 3
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updated = await ReadDocumentAsync(update);
        Assert.Equal("Alpha Updated", updated.RootElement.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(2, updated.RootElement.GetProperty("data").GetProperty("userCount").GetInt32());

        using var foreignUpdate = await client.PutAsJsonAsync("/api/outlets/20", new
        {
            name = "Foreign Updated",
            address = "No Access"
        });
        Assert.Equal(HttpStatusCode.NotFound, foreignUpdate.StatusCode);

        using var activeDelete = await client.DeleteAsync("/api/outlets/10");
        Assert.Equal(HttpStatusCode.BadRequest, activeDelete.StatusCode);
        using var activeDeleteBody = await ReadDocumentAsync(activeDelete);
        Assert.Equal("Cannot delete outlet. It has 2 active user(s)",
            activeDeleteBody.RootElement.GetProperty("error").GetString());

        var createdId = createdOutlet.GetProperty("id").GetInt64();
        using var delete = await client.DeleteAsync($"/api/outlets/{createdId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        using var deleted = await ReadDocumentAsync(delete);
        Assert.Equal("Outlet deleted successfully", deleted.RootElement.GetProperty("message").GetString());

        using var foreignDelete = await client.DeleteAsync("/api/outlets/20");
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
    }

    private HttpClient CreateClient(string permissions = "outlets.view")
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
        db.Users.AddRange(
            CreateUser(1, 17, 10, "Manager A"),
            CreateUser(2, 18, 20, "Manager B"),
            CreateUser(3, 17, 10, "Active A"),
            CreateUser(4, 18, 20, "Active B"),
            CreateUser(5, 17, 10, "Inactive A", false));
        db.Outlets.AddRange(
            CreateOutlet(10, 17, "Alpha", "Main Street", "555-0100", 1),
            CreateOutlet(20, 18, "Beta", "North Road", "555-0200", 2));
        await db.SaveChangesAsync();
    }

    private static Outlet CreateOutlet(
        long id,
        long businessId,
        string name,
        string address,
        string contact,
        long managerId) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            Name = name,
            Address = address,
            ContactNumber = contact,
            ManagerId = managerId
        };

    private static User CreateUser(
        long id,
        long businessId,
        long outletId,
        string name,
        bool isActive = true) =>
        new()
        {
            Id = id,
            BusinessId = businessId,
            OutletId = outletId,
            Name = name,
            Email = $"{id}@example.test",
            PasswordHash = "not-used",
            IsActive = isActive
        };

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
