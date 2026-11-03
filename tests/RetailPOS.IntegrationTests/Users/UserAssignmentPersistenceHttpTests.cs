using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Users;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Users;

public class UserAssignmentPersistenceHttpTests : IClassFixture<RetailPosApiFactory>
{
    private const long SuperAdminRoleId = 99000;
    private const long SuperAdminUserId = 999;
    private const long BusinessId = 5101;
    private const long OtherBusinessId = 5102;
    private const long RoleId = 5110;
    private const long Outlet1 = 51001;
    private const long Outlet2 = 51002;
    private const long Outlet3 = 51003;
    private const long OtherBusinessOutlet = 51099;
    private const long Warehouse1 = 52001;
    private const long Warehouse2 = 52002;

    private readonly RetailPosApiFactory _factory;

    public UserAssignmentPersistenceHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateEditSaveReload_PersistsAddsRemovesAndPreservesAssignments()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var createResponse = await client.PostAsJsonAsync("/api/users", new CreateUserDto
        {
            Name = "Assignment User",
            Email = "assignment.user@example.test",
            Password = "secret123",
            RoleId = RoleId,
            BusinessId = BusinessId,
            OutletIds = new[] { Outlet1, Outlet2 },
            WarehouseIds = new[] { Warehouse1, Warehouse2 },
            DefaultLocationType = "outlet",
            DefaultLocationId = Outlet1,
            InventoryLocationAccessScope = CreateUserDto.ScopeSpecificLocations,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<UserDto>();
        var userId = created!.Id;

        await AssertAssignmentsAsync(client, userId, [Outlet1, Outlet2], [Warehouse1, Warehouse2]);

        // Add Outlet3, remove Outlet1 and Warehouse1.
        var update = BuildUpdate(UpdateUserDto.ScopeSpecificLocations, Outlet2, [Outlet2, Outlet3], [Warehouse2]);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/users/{userId}", update)).StatusCode);
        await AssertAssignmentsAsync(client, userId, [Outlet2, Outlet3], [Warehouse2]);

        // Unrelated update that omits assignment collections keeps the saved assignments.
        var unrelated = BuildUpdate(UpdateUserDto.ScopeSpecificLocations, Outlet2, null, null);
        unrelated.Name = "Assignment User Renamed";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/users/{userId}", unrelated)).StatusCode);
        await AssertAssignmentsAsync(client, userId, [Outlet2, Outlet3], [Warehouse2]);

        // All-locations scope still persists explicit selections; an explicit empty list removes them.
        var allScope = BuildUpdate(UpdateUserDto.ScopeAllLocations, Outlet3, [Outlet3, Outlet1], []);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/users/{userId}", allScope)).StatusCode);
        await AssertAssignmentsAsync(client, userId, [Outlet1, Outlet3], []);
    }

    [Fact]
    public async Task Update_WithOutletFromAnotherBusiness_IsRejectedAndKeepsAssignments()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var createResponse = await client.PostAsJsonAsync("/api/users", new CreateUserDto
        {
            Name = "Scoped Assignment User",
            Email = "scoped.assignment.user@example.test",
            Password = "secret123",
            RoleId = RoleId,
            BusinessId = BusinessId,
            OutletIds = new[] { Outlet1 },
            WarehouseIds = new[] { Warehouse1 },
            DefaultLocationType = "outlet",
            DefaultLocationId = Outlet1,
            InventoryLocationAccessScope = CreateUserDto.ScopeSpecificLocations,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var userId = (await createResponse.Content.ReadFromJsonAsync<UserDto>())!.Id;

        var update = BuildUpdate(
            UpdateUserDto.ScopeSpecificLocations,
            Outlet1,
            [Outlet1, OtherBusinessOutlet],
            [Warehouse1]);
        update.Email = "scoped.assignment.user@example.test";
        var response = await client.PutAsJsonAsync($"/api/users/{userId}", update);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertAssignmentsAsync(client, userId, [Outlet1], [Warehouse1]);
    }

    private static UpdateUserDto BuildUpdate(
        string scope,
        long defaultOutletId,
        long[]? outletIds,
        long[]? warehouseIds) => new()
    {
        Name = "Assignment User",
        Email = "assignment.user@example.test",
        RoleId = RoleId,
        BusinessId = BusinessId,
        OutletIds = outletIds,
        WarehouseIds = warehouseIds,
        DefaultLocationType = "outlet",
        DefaultLocationId = defaultOutletId,
        InventoryLocationAccessScope = scope,
        IsActive = true
    };

    private static async Task AssertAssignmentsAsync(
        HttpClient client,
        long userId,
        long[] expectedOutletIds,
        long[] expectedWarehouseIds)
    {
        var user = await client.GetFromJsonAsync<UserDto>($"/api/users/{userId}");
        Assert.NotNull(user);
        Assert.Equal(
            expectedOutletIds.OrderBy(id => id),
            (user!.OutletAssignments ?? []).Select(a => a.OutletId).OrderBy(id => id));
        Assert.Equal(
            expectedWarehouseIds.OrderBy(id => id),
            (user.WarehouseAssignments ?? []).Select(a => a.WarehouseId).OrderBy(id => id));
    }

    private HttpClient CreateSuperAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "9999");
        client.DefaultRequestHeaders.Add("X-Test-UserId", SuperAdminUserId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "users.view,users.create,users.edit,*");
        return client;
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var now = DateTime.UtcNow;

        foreach (var businessId in new[] { BusinessId, OtherBusinessId })
        {
            if (!db.Businesses.Any(b => b.Id == businessId))
            {
                db.Businesses.Add(new Business
                {
                    Id = businessId,
                    Name = $"Business {businessId}",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        if (!db.Roles.Any(r => r.Id == RoleId))
        {
            db.Roles.Add(new Role { Id = RoleId, Name = "BusinessOwner", Permissions = """["*"]""" });
        }

        if (!db.Roles.Any(r => r.Id == SuperAdminRoleId))
        {
            db.Roles.Add(new Role { Id = SuperAdminRoleId, Name = "Super Admin", Permissions = """["*"]""" });
        }

        if (!db.Users.Any(u => u.Id == SuperAdminUserId))
        {
            db.Users.Add(new User
            {
                Id = SuperAdminUserId,
                Name = "Persisted Super Admin Test Actor",
                Email = "persisted-super-admin-assignment-tests@example.test",
                PasswordHash = "not-used",
                RoleId = SuperAdminRoleId,
                IsActive = true
            });
        }

        foreach (var (outletId, businessId) in new[]
        {
            (Outlet1, BusinessId), (Outlet2, BusinessId), (Outlet3, BusinessId), (OtherBusinessOutlet, OtherBusinessId)
        })
        {
            if (!db.Outlets.Any(o => o.Id == outletId))
            {
                db.Outlets.Add(new Outlet
                {
                    Id = outletId,
                    Name = $"Outlet {outletId}",
                    Address = "Address",
                    BusinessId = businessId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        foreach (var warehouseId in new[] { Warehouse1, Warehouse2 })
        {
            if (!db.Warehouses.Any(w => w.Id == warehouseId))
            {
                db.Warehouses.Add(new Warehouse
                {
                    Id = warehouseId,
                    Name = $"Warehouse {warehouseId}",
                    Address = "Address",
                    BusinessId = BusinessId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
