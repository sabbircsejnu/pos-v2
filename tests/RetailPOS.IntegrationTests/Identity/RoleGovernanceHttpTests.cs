using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RetailPOS.API.DTOs.Roles;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.Tests.Identity;

public sealed class RoleGovernanceHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public RoleGovernanceHttpTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateAndUpdate_PreserveCreatedResponseAndReplacePermissions()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("roles.view,roles.create,roles.edit");

        using var createResponse = await client.PostAsJsonAsync("/api/roles", new CreateRoleDto
        {
            Name = "Temporary Role",
            Permissions = ["sales.view", "sales.view", "inventory.adjust"]
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<RoleDto>();
        Assert.NotNull(created);
        Assert.EndsWith(
            $"/api/roles/{created.Id}",
            createResponse.Headers.Location?.OriginalString,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Temporary Role", created.Name);
        Assert.Equal(
            new[]
            {
                "sales.view",
                "stock_adjustments.view",
                "stock_adjustments.create",
                "stock_adjustments.edit",
                "stock_adjustments.delete",
                "stock_adjustments.approve",
                "stock_adjustments.reject",
                "stock_adjustments.cancel"
            },
            created.Permissions);

        using var updateResponse = await client.PutAsJsonAsync($"/api/roles/{created.Id}", new UpdateRoleDto
        {
            Name = "Temporary Role Renamed",
            Permissions = ["products.view"]
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<RoleDto>();
        Assert.NotNull(updated);
        Assert.Equal("Temporary Role Renamed", updated.Name);
        Assert.Equal(["products.view"], updated.Permissions);
    }

    [Fact]
    public async Task CreateAndUpdate_PreserveDuplicateAndInvalidPermissionErrors()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("roles.view,roles.create,roles.edit");

        using var duplicateResponse = await client.PostAsJsonAsync("/api/roles", new CreateRoleDto
        {
            Name = "Cashier",
            Permissions = ["sales.view"]
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        Assert.Equal(
            "Role with name 'Cashier' already exists",
            (await ReadJsonAsync(duplicateResponse)).RootElement.GetProperty("error").GetString());

        using var invalidPermissionResponse = await client.PostAsJsonAsync("/api/roles", new CreateRoleDto
        {
            Name = "Invalid Permission Role",
            Permissions = ["not.a.permission"]
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPermissionResponse.StatusCode);
        Assert.Equal(
            "Invalid permissions: not.a.permission",
            (await ReadJsonAsync(invalidPermissionResponse)).RootElement.GetProperty("error").GetString());

        using var duplicateUpdateResponse = await client.PutAsJsonAsync("/api/roles/1", new UpdateRoleDto
        {
            Name = "Unused Role",
            Permissions = ["sales.view"]
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateUpdateResponse.StatusCode);
        Assert.Equal(
            "Role with name 'Unused Role' already exists",
            (await ReadJsonAsync(duplicateUpdateResponse)).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ProtectedAndAssignedRolesKeepExistingRestrictions()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("roles.view,roles.create,roles.edit,roles.delete");

        using var createSuperAdminResponse = await client.PostAsJsonAsync("/api/roles", new CreateRoleDto
        {
            Name = "Super Admin",
            Permissions = ["*"]
        });
        Assert.Equal(HttpStatusCode.Forbidden, createSuperAdminResponse.StatusCode);

        using var updateSuperAdminResponse = await client.PutAsJsonAsync("/api/roles/2", new UpdateRoleDto
        {
            Name = "Super Admin",
            Permissions = ["*"]
        });
        Assert.Equal(HttpStatusCode.Forbidden, updateSuperAdminResponse.StatusCode);

        using var deleteSuperAdminResponse = await client.DeleteAsync("/api/roles/2");
        Assert.Equal(HttpStatusCode.BadRequest, deleteSuperAdminResponse.StatusCode);
        Assert.Equal(
            "Super Admin role cannot be deleted.",
            (await ReadJsonAsync(deleteSuperAdminResponse)).RootElement.GetProperty("error").GetString());

        using var deleteAssignedResponse = await client.DeleteAsync("/api/roles/1");
        Assert.Equal(HttpStatusCode.BadRequest, deleteAssignedResponse.StatusCode);
        Assert.Equal(
            "Cannot delete role 'Cashier' because it is assigned to users",
            (await ReadJsonAsync(deleteAssignedResponse)).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Delete_ReturnsExistingMessageAndMissingRoleIsNotFound()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("roles.view,roles.delete");

        using var response = await client.DeleteAsync("/api/roles/3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Role deleted successfully",
            (await ReadJsonAsync(response)).RootElement.GetProperty("message").GetString());

        using var missingResponse = await client.DeleteAsync("/api/roles/999");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(
            "Role with ID 999 not found",
            (await ReadJsonAsync(missingResponse)).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RoleWriteActions_RequireTheirExistingPermissionPolicies()
    {
        await ResetAndSeedAsync();
        using var client = CreateClient("roles.view");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/roles", new CreateRoleDto
            {
                Name = "Denied Role",
                Permissions = ["sales.view"]
            })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync("/api/roles/1", new UpdateRoleDto
            {
                Name = "Cashier",
                Permissions = ["sales.view"]
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/roles/3")).StatusCode);
    }

    [Fact]
    public void RoleGovernanceRoutesRemainInOpenApi()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var collection = swagger.Paths.Single(path =>
            string.Equals(path.Key, "/api/Roles", StringComparison.OrdinalIgnoreCase));
        var role = swagger.Paths.Single(path =>
            string.Equals(path.Key, "/api/Roles/{id}", StringComparison.OrdinalIgnoreCase));
        var permissions = swagger.Paths.Single(path =>
            string.Equals(path.Key, "/api/Roles/permissions", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(OperationType.Get, collection.Value.Operations.Keys);
        Assert.Contains(OperationType.Post, collection.Value.Operations.Keys);
        Assert.Contains(OperationType.Get, role.Value.Operations.Keys);
        Assert.Contains(OperationType.Put, role.Value.Operations.Keys);
        Assert.Contains(OperationType.Delete, role.Value.Operations.Keys);
        Assert.Contains(OperationType.Get, permissions.Value.Operations.Keys);
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Roles.AddRange(
            new Role { Id = 1, Name = "Cashier", Permissions = """["sales.view"]""" },
            new Role { Id = 2, Name = "Super Admin", Permissions = """["*"]""" },
            new Role { Id = 3, Name = "Unused Role", Permissions = """["products.view"]""" });
        db.Users.Add(new User
        {
            Id = 1,
            BusinessId = 1,
            Name = "Assigned User",
            Email = "assigned@example.test",
            PasswordHash = "not-returned",
            RoleId = 1,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
