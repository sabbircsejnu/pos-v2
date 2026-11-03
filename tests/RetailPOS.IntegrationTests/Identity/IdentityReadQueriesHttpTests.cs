using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.Tests.Identity;

public sealed class IdentityReadQueriesHttpTests : IClassFixture<RetailPosApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RetailPosApiFactory _factory;

    public IdentityReadQueriesHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRoles_ReturnsOrderedRolesWithActiveCallerBusinessCountsAndHiddenSuperAdmin()
    {
        await ResetAndSeedRolesAsync();
        using var client = CreateClient("roles.view", "AccountsAdmin", 1);

        using var response = await client.GetAsync("/api/roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        var roles = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(["Cashier", "Manager"], roles.Select(role => role.GetProperty("name").GetString()));
        Assert.Equal(1, roles[0].GetProperty("userCount").GetInt32());
        Assert.Equal(new[] { "sales.view", "pos.sell" },
            roles[0].GetProperty("permissions").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(0, roles[1].GetProperty("userCount").GetInt32());
    }

    [Fact]
    public async Task GetRoles_WithoutPermission_ReturnsForbidden()
    {
        using var client = CreateClient("users.view", "AccountsAdmin", 1);

        using var response = await client.GetAsync("/api/roles");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRoles_SuperAdminCanSeeSuperAdminRoleAndGlobalActiveCount()
    {
        await ResetAndSeedRolesAsync();
        using var client = CreateClient("roles.view", "Super Admin", 1);

        using var response = await client.GetAsync("/api/roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        var superAdminRole = document.RootElement.EnumerateArray().Single(role =>
            role.GetProperty("name").GetString() == "Super Admin");
        Assert.Equal(1, superAdminRole.GetProperty("userCount").GetInt32());
    }

    [Fact]
    public async Task GetUsers_AppliesBusinessSearchRoleOutletStatusAndPagingWithExistingProjection()
    {
        await ResetAndSeedUsersAsync();
        using var client = CreateClient("users.view", "AccountsAdmin", 1);

        using var firstResponse = await client.GetAsync(
            "/api/users?pageNumber=1&pageSize=1&search=Alice&roleId=9&outletId=0&isActive=true");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var firstDocument = await ReadDocumentAsync(firstResponse);
        var first = firstDocument.RootElement;
        Assert.Equal(3, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, first.GetProperty("pageNumber").GetInt32());
        Assert.Equal(1, first.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, first.GetProperty("totalPages").GetInt32());
        var firstUser = first.GetProperty("users").EnumerateArray().Single();
        Assert.Equal("Alice New", firstUser.GetProperty("name").GetString());
        Assert.Equal("Cashier", firstUser.GetProperty("roleName").GetString());
        Assert.Equal(1, firstUser.GetProperty("businessId").GetInt64());
        Assert.Null(firstUser.GetProperty("businessName").GetString());
        Assert.Equal(JsonValueKind.Null, firstUser.GetProperty("outletId").ValueKind);
        Assert.Equal("assigned_only", firstUser.GetProperty("inventoryLocationAccessScope").GetString());
        Assert.Equal("warehouse", firstUser.GetProperty("defaultLocationType").GetString());
        Assert.Equal(300, firstUser.GetProperty("defaultLocationId").GetInt64());
        Assert.Equal(new[] { "users.edit", "legacy.permission" },
            firstUser.GetProperty("userPermissions").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("Main Outlet", firstUser.GetProperty("outletAssignments")[0].GetProperty("outletName").GetString());
        Assert.Equal("Main Warehouse", firstUser.GetProperty("warehouseAssignments")[0].GetProperty("warehouseName").GetString());
        Assert.False(firstUser.TryGetProperty("passwordHash", out _));
        Assert.False(firstUser.TryGetProperty("permissionOverrides", out _));
        Assert.False(firstUser.TryGetProperty("refreshTokens", out _));
        Assert.False(firstUser.TryGetProperty("invitations", out _));

        using var secondResponse = await client.GetAsync(
            "/api/users?pageNumber=2&pageSize=1&search=Alice&roleId=9&outletId=0&isActive=true");

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var secondDocument = await ReadDocumentAsync(secondResponse);
        Assert.Equal(
            "Alice Middle",
            secondDocument.RootElement.GetProperty("users")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetUserById_PreservesDetailProjectionAndBusinessFence()
    {
        await ResetAndSeedUsersAsync();
        using var client = CreateClient("users.view", "AccountsAdmin", 1);

        using var response = await client.GetAsync("/api/users/3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        var user = document.RootElement;
        Assert.Equal(3, user.GetProperty("id").GetInt64());
        Assert.Equal("Alice New", user.GetProperty("name").GetString());
        Assert.Equal("alice.new@example.test", user.GetProperty("email").GetString());
        Assert.Equal("Cashier", user.GetProperty("roleName").GetString());
        Assert.Equal(1, user.GetProperty("businessId").GetInt64());
        Assert.Equal("assigned_only", user.GetProperty("inventoryLocationAccessScope").GetString());
        Assert.Equal(101, user.GetProperty("outletAssignments")[0].GetProperty("outletId").GetInt64());
        Assert.Equal(300, user.GetProperty("warehouseAssignments")[0].GetProperty("warehouseId").GetInt64());
        Assert.False(user.TryGetProperty("passwordHash", out _));

        using var foreignResponse = await client.GetAsync("/api/users/5");
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        using var foreignDocument = await ReadDocumentAsync(foreignResponse);
        Assert.Equal(
            "User with ID 5 not found",
            foreignDocument.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetUsersByOutletAndRole_PreserveActiveBusinessScopedLists()
    {
        await ResetAndSeedUsersAsync();
        using var client = CreateClient("users.view", "AccountsAdmin", 1);

        using var outletResponse = await client.GetAsync("/api/users/outlet/101");
        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        using var outletDocument = await ReadDocumentAsync(outletResponse);
        Assert.Equal(
            ["Bob Outlet"],
            outletDocument.RootElement.EnumerateArray()
                .Select(user => user.GetProperty("name").GetString()));

        using var roleResponse = await client.GetAsync("/api/users/role/9");
        Assert.Equal(HttpStatusCode.OK, roleResponse.StatusCode);
        using var roleDocument = await ReadDocumentAsync(roleResponse);
        Assert.Equal(
            ["Alice Middle", "Alice New", "Alice Old"],
            roleDocument.RootElement.EnumerateArray()
                .Select(user => user.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task GetRoleByIdAndPermissions_PreserveVisibilityCountAndCatalog()
    {
        await ResetAndSeedRolesAsync();
        using var client = CreateClient("roles.view", "AccountsAdmin", 1);

        using var roleResponse = await client.GetAsync("/api/roles/2");
        Assert.Equal(HttpStatusCode.OK, roleResponse.StatusCode);
        using var roleDocument = await ReadDocumentAsync(roleResponse);
        Assert.Equal("Cashier", roleDocument.RootElement.GetProperty("name").GetString());
        Assert.Equal(2, roleDocument.RootElement.GetProperty("userCount").GetInt32());
        Assert.Equal(
            ["sales.view", "pos.sell"],
            roleDocument.RootElement.GetProperty("permissions").EnumerateArray()
                .Select(permission => permission.GetString()));

        using var hiddenResponse = await client.GetAsync("/api/roles/3");
        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);
        using var hiddenDocument = await ReadDocumentAsync(hiddenResponse);
        Assert.Equal(
            "Role with ID 3 not found",
            hiddenDocument.RootElement.GetProperty("error").GetString());

        using var superAdminClient = CreateClient("roles.view", "Super Admin", 1);
        using var superAdminResponse = await superAdminClient.GetAsync("/api/roles/3");
        Assert.Equal(HttpStatusCode.OK, superAdminResponse.StatusCode);
        using var superAdminDocument = await ReadDocumentAsync(superAdminResponse);
        Assert.Equal("Super Admin", superAdminDocument.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, superAdminDocument.RootElement.GetProperty("userCount").GetInt32());

        using var permissionsResponse = await client.GetAsync("/api/roles/permissions");
        Assert.Equal(HttpStatusCode.OK, permissionsResponse.StatusCode);
        using var permissionsDocument = await ReadDocumentAsync(permissionsResponse);
        Assert.Equal(
            PermissionCatalog.All,
            permissionsDocument.RootElement.EnumerateArray()
                .Select(permission => permission.GetString()));
    }

    [Fact]
    public async Task GetUsers_OutletFilterAndEmptySearchResultPreserveResponseMetadata()
    {
        await ResetAndSeedUsersAsync();
        using var client = CreateClient("users.view", "AccountsAdmin", 1);

        using var outletResponse = await client.GetAsync("/api/users?outletId=101");
        Assert.Equal(HttpStatusCode.OK, outletResponse.StatusCode);
        using var outletDocument = await ReadDocumentAsync(outletResponse);
        var outletResult = outletDocument.RootElement;
        Assert.Equal(1, outletResult.GetProperty("totalCount").GetInt32());
        Assert.Equal("Bob Outlet", outletResult.GetProperty("users")[0].GetProperty("name").GetString());
        Assert.Equal("Main Outlet", outletResult.GetProperty("users")[0].GetProperty("outletName").GetString());

        using var emptyResponse = await client.GetAsync("/api/users?search=No%20Such%20User");
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        using var emptyDocument = await ReadDocumentAsync(emptyResponse);
        Assert.Empty(emptyDocument.RootElement.GetProperty("users").EnumerateArray());
        Assert.Equal(0, emptyDocument.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, emptyDocument.RootElement.GetProperty("pageNumber").GetInt32());
        Assert.Equal(10, emptyDocument.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(0, emptyDocument.RootElement.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task GetUsers_WithoutPermission_ReturnsForbidden()
    {
        using var client = CreateClient("roles.view", "AccountsAdmin", 1);

        using var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_SuperAdminReceivesUsersAcrossBusinesses()
    {
        await ResetAndSeedUsersAsync();
        using var client = CreateClient("users.view", "Super Admin", 1);

        using var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await ReadDocumentAsync(response);
        var root = document.RootElement;
        Assert.Equal(6, root.GetProperty("totalCount").GetInt32());
        Assert.Contains(root.GetProperty("users").EnumerateArray(), user =>
            user.GetProperty("name").GetString() == "Alice Other Business");
    }

    [Fact]
    public void RoleAndUserListRoutesRemainInOpenApi()
    {
        var swagger = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        foreach (var route in new[] { "/api/Roles", "/api/Users" })
        {
            var path = swagger.Paths.Single(item =>
                string.Equals(item.Key, route, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(OperationType.Get, path.Value.Operations.Keys);
        }
    }

    private HttpClient CreateClient(string permissions, string role, long businessId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", businessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task ResetAndSeedRolesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Roles.AddRange(
            new Role { Id = 1, Name = "Manager", Permissions = """["users.view"]""" },
            new Role { Id = 2, Name = "Cashier", Permissions = """["sales.view","pos.sell"]""" },
            new Role { Id = 3, Name = "Super Admin", Permissions = """["*"]""" });
        db.Users.AddRange(
            new User { Id = 1, BusinessId = 1, Name = "Active Biz One", Email = "a1@example.test", PasswordHash = "secret", RoleId = 2, IsActive = true },
            new User { Id = 2, BusinessId = 1, Name = "Inactive Biz One", Email = "i1@example.test", PasswordHash = "secret", RoleId = 2, IsActive = false },
            new User { Id = 3, BusinessId = 2, Name = "Active Biz Two", Email = "a2@example.test", PasswordHash = "secret", RoleId = 2, IsActive = true },
            new User { Id = 4, BusinessId = 2, Name = "Super Admin User", Email = "sa@example.test", PasswordHash = "secret", RoleId = 3, IsActive = true });
        await db.SaveChangesAsync();
    }

    private async Task ResetAndSeedUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        db.Roles.AddRange(
            new Role { Id = 9, Name = "Cashier", Permissions = """["sales.view"]""" },
            new Role { Id = 10, Name = "Manager", Permissions = """["users.view"]""" });
        db.Outlets.Add(new Outlet { Id = 101, BusinessId = 1, Name = "Main Outlet", Address = "A" });
        db.Warehouses.Add(new Warehouse { Id = 300, BusinessId = 1, Name = "Main Warehouse", Address = "A" });
        db.Users.AddRange(
            User(1, 1, "Alice Old", "alice.old@example.test", 9, null, true, new DateTime(2024, 1, 1)),
            User(2, 1, "Alice Middle", "alice.middle@example.test", 9, null, true, new DateTime(2024, 2, 1)),
            User(3, 1, "Alice New", "alice.new@example.test", 9, null, true, new DateTime(2024, 3, 1)),
            User(4, 1, "Alice Inactive", "alice.inactive@example.test", 9, null, false, new DateTime(2024, 4, 1)),
            User(5, 2, "Alice Other Business", "alice.other@example.test", 9, null, true, new DateTime(2024, 5, 1)),
            User(6, 1, "Bob Outlet", "bob.outlet@example.test", 10, 101, true, new DateTime(2024, 6, 1)));
        db.UserOutletAssignments.Add(new UserOutletAssignment
        {
            Id = 1,
            UserId = 3,
            OutletId = 101,
            BusinessId = 1,
            IsPrimary = true,
            IsActive = true
        });
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment
        {
            Id = 1,
            UserId = 3,
            WarehouseId = 300,
            BusinessId = 1,
            IsPrimary = true,
            IsActive = true
        });
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment
        {
            Id = 2,
            UserId = 2,
            WarehouseId = 300,
            BusinessId = 1,
            IsPrimary = true,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static User User(
        long id,
        long businessId,
        string name,
        string email,
        long roleId,
        long? outletId,
        bool isActive,
        DateTime createdAt) => new()
    {
        Id = id,
        BusinessId = businessId,
        Name = name,
        Email = email,
        PasswordHash = "do-not-return-this",
        RoleId = roleId,
        OutletId = outletId,
        PermissionOverrides = """["users.edit","legacy.permission"]""",
        InventoryLocationAccessScope = " ",
        IsActive = isActive,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}
