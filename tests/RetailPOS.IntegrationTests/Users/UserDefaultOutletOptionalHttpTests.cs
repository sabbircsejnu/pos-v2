using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Users;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Users;

public class UserDefaultOutletOptionalHttpTests : IClassFixture<RetailPosApiFactory>
{
    private const long SuperAdminRoleId = 99000;
    private const long SuperAdminUserId = 999;
    private const long BusinessId = 6101;
    private const long OtherBusinessId = 6102;
    private const long CustomRoleId = 6110;
    private const long WarehouseManagerRoleId = 6111;
    private const long CashierRoleId = 6112;
    private const long OwnerRoleId = 6113;
    private const long Outlet1 = 61001;
    private const long Outlet2 = 61002;
    private const long OtherBusinessOutlet = 61099;
    private const long Warehouse1 = 62001;
    private const long Warehouse2 = 62002;
    private const long OtherBusinessWarehouse = 62099;

    private readonly RetailPosApiFactory _factory;

    public UserDefaultOutletOptionalHttpTests(RetailPosApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_CustomRoleWarehouseOnly_WithoutDefault_LeavesDefaultOutletNull()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var user = await CreateAsync(client, Dto("wh-only@example.test", CustomRoleId, warehouseIds: [Warehouse1]));

        Assert.Null(user.OutletId);
        Assert.Empty(user.OutletAssignments ?? []);
        Assert.Equal("warehouse", user.DefaultLocationType);
        Assert.Equal(Warehouse1, user.DefaultLocationId);
    }

    [Fact]
    public async Task Create_WarehouseManager_WithDefaultWarehouse_KeepsChosenWarehouseAsDefault()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var dto = Dto("wh-manager@example.test", WarehouseManagerRoleId, warehouseIds: [Warehouse1, Warehouse2]);
        dto.DefaultLocationType = "warehouse";
        dto.DefaultLocationId = Warehouse2;
        var user = await CreateAsync(client, dto);

        Assert.Null(user.OutletId);
        Assert.Equal("warehouse", user.DefaultLocationType);
        Assert.Equal(Warehouse2, user.DefaultLocationId);
    }

    [Fact]
    public async Task Create_WarehouseManager_WithoutWarehouse_IsStillRejected()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var response = await client.PostAsJsonAsync(
            "/api/users",
            Dto("wh-manager-empty@example.test", WarehouseManagerRoleId, scope: CreateUserDto.ScopeAllLocations));

        await AssertBadRequestContainsAsync(response, "at least one warehouse");
    }

    [Fact]
    public async Task Create_OutletCashier_WithValidDefaultOutlet_PersistsDefaultOutlet()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var dto = Dto("cashier@example.test", CashierRoleId, outletIds: [Outlet1]);
        dto.DefaultLocationType = "outlet";
        dto.DefaultLocationId = Outlet1;
        var user = await CreateAsync(client, dto);

        Assert.Equal(Outlet1, user.OutletId);
    }

    [Fact]
    public async Task Create_OutletCashier_WithoutDefaultOutlet_IsRejected()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var response = await client.PostAsJsonAsync(
            "/api/users",
            Dto("cashier-no-default@example.test", CashierRoleId, outletIds: [Outlet1]));

        await AssertBadRequestContainsAsync(response, "default outlet");
    }

    [Fact]
    public async Task Create_MultipleOutlets_WithoutDefault_DoesNotPromoteFirstOutlet()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var user = await CreateAsync(
            client,
            Dto(
                "multi-outlet@example.test",
                CustomRoleId,
                outletIds: [Outlet1, Outlet2],
                scope: CreateUserDto.ScopeSpecificLocations));

        Assert.Null(user.OutletId);
        Assert.Null(user.DefaultLocationId);
        Assert.Equal(2, (user.OutletAssignments ?? []).Count());
    }

    [Theory]
    [InlineData(CreateUserDto.ScopeSpecificLocations)]
    [InlineData(CreateUserDto.ScopeAssignedOnly)]
    public async Task Create_DefaultOutletOutsideExplicitAssignments_IsRejected(string scope)
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var dto = Dto($"outside-{scope}@example.test", CustomRoleId, outletIds: [Outlet1], scope: scope);
        dto.DefaultLocationType = "outlet";
        dto.DefaultLocationId = Outlet2;
        var response = await client.PostAsJsonAsync("/api/users", dto);

        await AssertBadRequestContainsAsync(response, "assigned");
    }

    [Fact]
    public async Task Create_AllLocations_DefaultOutletOutsideAssignments_IsAllowed()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var dto = Dto("all-scope@example.test", CustomRoleId, outletIds: [Outlet1], scope: CreateUserDto.ScopeAllLocations);
        dto.DefaultLocationType = "outlet";
        dto.DefaultLocationId = Outlet2;
        var user = await CreateAsync(client, dto);

        Assert.Equal(Outlet2, user.OutletId);
        Assert.Equal([Outlet1], (user.OutletAssignments ?? []).Select(a => a.OutletId));
    }

    [Fact]
    public async Task Create_BusinessOwner_WithoutDefault_AndOutsideAssignments_IsAllowed()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var withoutDefault = await CreateAsync(
            client,
            Dto("owner-none@example.test", OwnerRoleId, scope: CreateUserDto.ScopeAllLocations));
        Assert.Null(withoutDefault.OutletId);

        var dto = Dto("owner-outside@example.test", OwnerRoleId, outletIds: [Outlet1], scope: CreateUserDto.ScopeSpecificLocations);
        dto.DefaultLocationType = "outlet";
        dto.DefaultLocationId = Outlet2;
        var outside = await CreateAsync(client, dto);
        Assert.Equal(Outlet2, outside.OutletId);
    }

    [Theory]
    [InlineData("outlet", OtherBusinessOutlet)]
    [InlineData("warehouse", OtherBusinessWarehouse)]
    public async Task Create_DefaultLocationFromAnotherBusiness_IsForbidden(string type, long locationId)
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var dto = Dto($"cross-{type}@example.test", CustomRoleId, scope: CreateUserDto.ScopeAllLocations);
        dto.DefaultLocationType = type;
        dto.DefaultLocationId = locationId;
        var response = await client.PostAsJsonAsync("/api/users", dto);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_ClearingDefaultOutlet_KeepsAssignmentsAndDoesNotPromoteAnotherOutlet()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var dto = Dto(
            "clear-default@example.test",
            CustomRoleId,
            outletIds: [Outlet1, Outlet2],
            scope: CreateUserDto.ScopeSpecificLocations);
        dto.DefaultLocationType = "outlet";
        dto.DefaultLocationId = Outlet1;
        var created = await CreateAsync(client, dto);
        Assert.Equal(Outlet1, created.OutletId);

        var response = await client.PutAsJsonAsync($"/api/users/{created.Id}", new UpdateUserDto
        {
            Name = created.Name,
            Email = created.Email,
            RoleId = CustomRoleId,
            BusinessId = BusinessId,
            OutletIds = [Outlet1, Outlet2],
            WarehouseIds = [],
            InventoryLocationAccessScope = UpdateUserDto.ScopeSpecificLocations,
            IsActive = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reloaded = await client.GetFromJsonAsync<UserDto>($"/api/users/{created.Id}");
        Assert.Null(reloaded!.OutletId);
        Assert.Null(reloaded.DefaultLocationId);
        Assert.Equal(2, (reloaded.OutletAssignments ?? []).Count());
    }

    [Fact]
    public async Task Update_UnrelatedChange_PreservesExistingDefaultOutlet()
    {
        await SeedAsync();
        using var client = CreateSuperAdminClient();

        var dto = Dto("preserve-default@example.test", CashierRoleId, outletIds: [Outlet1, Outlet2],
            scope: CreateUserDto.ScopeSpecificLocations);
        dto.DefaultLocationType = "outlet";
        dto.DefaultLocationId = Outlet2;
        var created = await CreateAsync(client, dto);

        var response = await client.PutAsJsonAsync($"/api/users/{created.Id}", new UpdateUserDto
        {
            Name = "Renamed",
            Email = created.Email,
            RoleId = CashierRoleId,
            BusinessId = BusinessId,
            OutletId = created.OutletId,
            DefaultLocationType = created.DefaultLocationType,
            DefaultLocationId = created.DefaultLocationId,
            InventoryLocationAccessScope = UpdateUserDto.ScopeSpecificLocations,
            IsActive = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reloaded = await client.GetFromJsonAsync<UserDto>($"/api/users/{created.Id}");
        Assert.Equal(Outlet2, reloaded!.OutletId);
    }

    [Fact]
    public async Task CurrentStockReport_WarehouseOnlyUser_CanFilterAssignedWarehouse_ButNotOtherLocations()
    {
        await SeedAsync();
        using var admin = CreateSuperAdminClient();
        var user = await CreateAsync(admin, Dto("wh-report@example.test", CustomRoleId, warehouseIds: [Warehouse1]));

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", BusinessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", user.Id.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Warehouse Clerk");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "reports.inventory");

        var assigned = await client.GetAsync($"/api/reports/inventory/current-stock?warehouseId={Warehouse1}");
        var otherWarehouse = await client.GetAsync($"/api/reports/inventory/current-stock?warehouseId={Warehouse2}");
        var outlet = await client.GetAsync($"/api/reports/inventory/current-stock?outletId={Outlet1}");
        var unfiltered = await client.GetAsync("/api/reports/inventory/current-stock");

        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherWarehouse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, outlet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unfiltered.StatusCode);
    }

    private static CreateUserDto Dto(
        string email,
        long roleId,
        long[]? outletIds = null,
        long[]? warehouseIds = null,
        string scope = CreateUserDto.ScopeAssignedOnly) => new()
    {
        Name = "Location Test User",
        Email = email,
        Password = "secret123",
        RoleId = roleId,
        BusinessId = BusinessId,
        OutletIds = outletIds ?? [],
        WarehouseIds = warehouseIds ?? [],
        InventoryLocationAccessScope = scope,
        IsActive = true
    };

    private static async Task<UserDto> CreateAsync(HttpClient client, CreateUserDto dto)
    {
        var response = await client.PostAsJsonAsync("/api/users", dto);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {body}");
        return (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }

    private static async Task AssertBadRequestContainsAsync(HttpResponseMessage response, string expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{(int)response.StatusCode}: {body}");
        Assert.Contains(expected, body, StringComparison.OrdinalIgnoreCase);
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

        foreach (var (roleId, roleName) in new[]
        {
            (SuperAdminRoleId, "Super Admin"),
            (CustomRoleId, "Warehouse Clerk"),
            (WarehouseManagerRoleId, "WarehouseManager"),
            (CashierRoleId, "Cashier"),
            (OwnerRoleId, "BusinessOwner")
        })
        {
            if (!db.Roles.Any(r => r.Id == roleId))
            {
                db.Roles.Add(new Role { Id = roleId, Name = roleName, Permissions = "[]" });
            }
        }

        if (!db.Users.Any(u => u.Id == SuperAdminUserId))
        {
            db.Users.Add(new User
            {
                Id = SuperAdminUserId,
                Name = "Persisted Super Admin Test Actor",
                Email = "persisted-super-admin-default-outlet-tests@example.test",
                PasswordHash = "not-used",
                RoleId = SuperAdminRoleId,
                IsActive = true
            });
        }

        foreach (var (outletId, businessId) in new[]
        {
            (Outlet1, BusinessId), (Outlet2, BusinessId), (OtherBusinessOutlet, OtherBusinessId)
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

        foreach (var (warehouseId, businessId) in new[]
        {
            (Warehouse1, BusinessId), (Warehouse2, BusinessId), (OtherBusinessWarehouse, OtherBusinessId)
        })
        {
            if (!db.Warehouses.Any(w => w.Id == warehouseId))
            {
                db.Warehouses.Add(new Warehouse
                {
                    Id = warehouseId,
                    Name = $"Warehouse {warehouseId}",
                    Address = "Address",
                    BusinessId = businessId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
