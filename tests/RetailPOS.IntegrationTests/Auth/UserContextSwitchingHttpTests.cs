using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Auth;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Auth;

public sealed class UserContextSwitchingHttpTests : IDisposable
{
    private const long BusinessId = 9101;
    private const long ForeignBusinessId = 9102;
    private const long OwnerRole = 9201, ClerkRole = 9202, CashierRole = 9203, ForeignRole = 9204, WildcardRole = 9205, SuperAdminRole = 9206;
    private const long Outlet1 = 9301, Outlet2 = 9302, ForeignOutlet = 9399;
    private const long Warehouse1 = 9401, Warehouse2 = 9402, ForeignWarehouse = 9499;
    private const long OwnerUser = 9501, SubjectUser = 9502, WildcardHolder = 9503, ForeignHolder = 9504, SuperAdminUser = 9505;

    private readonly RetailPosApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task WarehouseOnlyUser_WithNoDefaultOutlet_SeesOnlyTheirWarehouse_AndItIsAutoSelected()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, warehouses: [Warehouse1]);
        using var client = Client(SubjectUser, "Warehouse Clerk");

        var options = await OptionsAsync(client);

        Assert.True(options.CanSwitch);
        Assert.False(options.AllowAllLocations);
        Assert.Empty(options.Outlets);
        Assert.Equal([Warehouse1], options.Warehouses.Select(location => location.Id));

        var switched = await SwitchAsync(client, new RoleSwitchRequestDto { ActingRole = "Warehouse Clerk" });

        Assert.Equal("warehouse", switched.User.ActingLocationType);
        Assert.Equal(Warehouse1, switched.User.ActingWarehouseId);
        Assert.Null(switched.User.ActingOutletId);
    }

    [Fact]
    public async Task OutletOnlyUser_KeepsDefaultOutlet_AndItIsAutoSelected()
    {
        await SeedAsync();
        await AddSubjectAsync(CashierRole, defaultOutlet: Outlet1);
        using var client = Client(SubjectUser, "Cashier");

        var switched = await SwitchAsync(client, new RoleSwitchRequestDto { ActingRole = "Cashier" });

        Assert.Equal("outlet", switched.User.ActingLocationType);
        Assert.Equal(Outlet1, switched.User.ActingOutletId);
    }

    [Fact]
    public async Task MultiLocationUser_MustChoose_AndCanChooseAnyAssignedLocation()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, outlets: [Outlet1], warehouses: [Warehouse1]);
        using var client = Client(SubjectUser, "Warehouse Clerk");

        using var unspecified = await client.PostAsJsonAsync(
            "/api/auth/switch-role",
            new RoleSwitchRequestDto { ActingRole = "Warehouse Clerk" });
        Assert.Equal(HttpStatusCode.BadRequest, unspecified.StatusCode);

        var atWarehouse = await SwitchAsync(client, new RoleSwitchRequestDto
        {
            ActingRole = "Warehouse Clerk",
            ActingLocationType = "warehouse",
            ActingLocationId = Warehouse1
        });
        Assert.Equal(Warehouse1, atWarehouse.User.ActingWarehouseId);

        var atOutlet = await SwitchAsync(client, new RoleSwitchRequestDto
        {
            ActingRole = "Warehouse Clerk",
            ActingOutletId = Outlet1
        });
        Assert.Equal(Outlet1, atOutlet.User.ActingOutletId);
    }

    [Theory]
    [InlineData("outlet", Outlet2)]
    [InlineData("outlet", ForeignOutlet)]
    [InlineData("warehouse", Warehouse2)]
    [InlineData("warehouse", ForeignWarehouse)]
    public async Task SelectedLocations_RejectsCraftedUnassignedOrForeignLocation(string type, long locationId)
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, outlets: [Outlet1], warehouses: [Warehouse1]);
        using var client = Client(SubjectUser, "Warehouse Clerk");

        using var response = await client.PostAsJsonAsync("/api/auth/switch-role", new RoleSwitchRequestDto
        {
            ActingRole = "Warehouse Clerk",
            ActingLocationType = type,
            ActingLocationId = locationId
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SelectedLocations_EmptyAssignmentList_IsNeverAllLocations()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole);
        using var client = Client(SubjectUser, "Warehouse Clerk");

        var options = await OptionsAsync(client);
        Assert.False(options.CanSwitch);
        Assert.Empty(options.Outlets);
        Assert.Empty(options.Warehouses);

        using var response = await client.PostAsJsonAsync("/api/auth/switch-role", new RoleSwitchRequestDto
        {
            ActingRole = "Warehouse Clerk",
            ActingLocationType = "all"
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SelectedLocations_CannotActivateAllLocationsContext()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, outlets: [Outlet1]);
        using var client = Client(SubjectUser, "Warehouse Clerk");

        using var response = await client.PostAsJsonAsync("/api/auth/switch-role", new RoleSwitchRequestDto
        {
            ActingRole = "Warehouse Clerk",
            ActingLocationType = "all"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AllLocationsUser_SeesEveryLocationOfOwnBusinessOnly()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, locationScope: User.ContextLocationAll);
        using var client = Client(SubjectUser, "Warehouse Clerk");

        var options = await OptionsAsync(client);

        Assert.True(options.AllowAllLocations);
        Assert.Equal([Outlet1, Outlet2], options.Outlets.Select(location => location.Id).Order());
        Assert.Equal([Warehouse1, Warehouse2], options.Warehouses.Select(location => location.Id).Order());

        var switched = await SwitchAsync(client, new RoleSwitchRequestDto
        {
            ActingRole = "Warehouse Clerk",
            ActingLocationType = "all"
        });
        Assert.Equal("all", switched.User.ActingLocationType);
        Assert.Null(switched.User.ActingOutletId);
        Assert.Null(switched.User.ActingWarehouseId);

        using var foreign = await client.PostAsJsonAsync("/api/auth/switch-role", new RoleSwitchRequestDto
        {
            ActingRole = "Warehouse Clerk",
            ActingOutletId = ForeignOutlet
        });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    [Fact]
    public async Task MappedRoles_AllowOnlyOwnAndMappedRoles_WithThatRolesPermissionsOnly()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, outlets: [Outlet1], mappedRoles: [CashierRole]);
        using var client = Client(SubjectUser, "Warehouse Clerk");

        var options = await OptionsAsync(client);
        Assert.Equal(["Warehouse Clerk", "Cashier"], options.Roles.Select(role => role.Name));

        var switched = await SwitchAsync(client, new RoleSwitchRequestDto { ActingRole = "Cashier" });
        Assert.Equal("Cashier", switched.User.ActingRoleName);
        Assert.Equal("Warehouse Clerk", switched.User.RealRoleName);
        Assert.False(switched.User.IsBusinessOwner);
        Assert.Equal(["sales.create"], switched.User.Permissions);
        Assert.DoesNotContain("override.only", switched.User.Permissions);

        var ownRole = await SwitchAsync(client, new RoleSwitchRequestDto { ActingRole = "Warehouse Clerk" });
        Assert.Contains("override.only", ownRole.User.Permissions);
    }

    [Theory]
    [InlineData("BusinessOwner")]
    [InlineData("Super Admin")]
    [InlineData("Wildcard Custom")]
    [InlineData("Foreign Custom")]
    [InlineData("Cashier")]
    [InlineData("No Such Role")]
    public async Task MappedRoles_RejectEscalationThroughCraftedRoleName(string roleName)
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, outlets: [Outlet1]);
        using var client = Client(SubjectUser, "Warehouse Clerk");

        using var response = await client.PostAsJsonAsync(
            "/api/auth/switch-role",
            new RoleSwitchRequestDto { ActingRole = roleName });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AllRolesUser_GetsDefaultAndInUseRoles_ButNeverPrivilegedOrForeignRoles()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, outlets: [Outlet1], roleScope: User.ContextRoleAll);
        using var client = Client(SubjectUser, "Warehouse Clerk");

        var names = (await OptionsAsync(client)).Roles.Select(role => role.Name).ToArray();

        Assert.Contains("Cashier", names);
        Assert.Contains("Warehouse Clerk", names);
        Assert.DoesNotContain("BusinessOwner", names);
        Assert.DoesNotContain("Super Admin", names);
        Assert.DoesNotContain("Wildcard Custom", names);
        Assert.DoesNotContain("Foreign Custom", names);
    }

    [Fact]
    public async Task BusinessOwner_NeedsNoConfiguration_AndStaysInsideOwnBusiness()
    {
        await SeedAsync();
        using var client = Client(OwnerUser, "BusinessOwner");

        var options = await OptionsAsync(client);
        Assert.True(options.CanSwitch);
        Assert.True(options.AllowAllLocations);
        Assert.Equal([Outlet1, Outlet2], options.Outlets.Select(location => location.Id).Order());
        Assert.Equal([Warehouse1, Warehouse2], options.Warehouses.Select(location => location.Id).Order());
        Assert.DoesNotContain(options.Roles, role => role.Name is "BusinessOwner" or "Super Admin" or "Foreign Custom" or "Wildcard Custom");

        var atWarehouse = await SwitchAsync(client, new RoleSwitchRequestDto
        {
            ActingRole = "Cashier",
            ActingLocationType = "warehouse",
            ActingLocationId = Warehouse2
        });
        Assert.True(atWarehouse.User.IsBusinessOwner);
        Assert.Equal(Warehouse2, atWarehouse.User.ActingWarehouseId);

        using var foreign = await client.PostAsJsonAsync("/api/auth/switch-role", new RoleSwitchRequestDto
        {
            ActingRole = "Cashier",
            ActingLocationType = "warehouse",
            ActingLocationId = ForeignWarehouse
        });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using var selfRole = await client.PostAsJsonAsync(
            "/api/auth/switch-role",
            new RoleSwitchRequestDto { ActingRole = "BusinessOwner" });
        Assert.Equal(HttpStatusCode.Forbidden, selfRole.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CannotSwitchIntoABusinessContext()
    {
        await SeedAsync();
        using var client = Client(SuperAdminUser, "Super Admin");

        Assert.False((await OptionsAsync(client)).CanSwitch);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/switch-role",
            new RoleSwitchRequestDto { ActingRole = "Cashier", ActingOutletId = Outlet1 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutSwitchingEnabled_IsDenied_EvenForOwnAssignedLocation()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, outlets: [Outlet1], enabled: false, mappedRoles: [CashierRole]);
        using var client = Client(SubjectUser, "Warehouse Clerk");

        Assert.False((await OptionsAsync(client)).CanSwitch);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/switch-role",
            new RoleSwitchRequestDto { ActingRole = "Warehouse Clerk", ActingOutletId = Outlet1 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var returning = await client.PostAsync("/api/auth/return-owner", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, returning.StatusCode);
    }

    [Fact]
    public async Task UnsupportedLocationType_IsRejected()
    {
        await SeedAsync();
        using var client = Client(OwnerUser, "BusinessOwner");

        using var response = await client.PostAsJsonAsync("/api/auth/switch-role", new RoleSwitchRequestDto
        {
            ActingRole = "Cashier",
            ActingLocationType = "factory",
            ActingLocationId = 1
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_KeepsContext_WhileItIsStillAuthorized()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, outlets: [Outlet1], warehouses: [Warehouse1], mappedRoles: [CashierRole]);
        using var client = Client(SubjectUser, "Warehouse Clerk");
        var switched = await SwitchAsync(client, new RoleSwitchRequestDto
        {
            ActingRole = "Cashier",
            ActingLocationType = "warehouse",
            ActingLocationId = Warehouse1
        });

        var refreshed = await RefreshAsync(client, switched);

        Assert.True(refreshed.User.IsRoleSwitched);
        Assert.Equal("Cashier", refreshed.User.ActingRoleName);
        Assert.Equal(Warehouse1, refreshed.User.ActingWarehouseId);
        Assert.Equal(["sales.create"], refreshed.User.Permissions);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(refreshed.Token).Claims.ToArray();
        Assert.Contains(claims, claim => claim.Type == RoleSwitchClaims.ActingWarehouseId && claim.Value == Warehouse1.ToString());
        Assert.Contains(claims, claim => claim.Type == RoleSwitchClaims.RealRoleName && claim.Value == "Warehouse Clerk");
    }

    [Fact]
    public async Task Refresh_AfterAssignmentRemoved_FallsBackToOwnContext()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, outlets: [Outlet1, Outlet2]);
        using var client = Client(SubjectUser, "Warehouse Clerk");
        var switched = await SwitchAsync(client, new RoleSwitchRequestDto
        {
            ActingRole = "Warehouse Clerk",
            ActingOutletId = Outlet2
        });

        await WithDbAsync(async db =>
        {
            db.UserOutletAssignments.RemoveRange(db.UserOutletAssignments.Where(a => a.UserId == SubjectUser && a.OutletId == Outlet2));
            await db.SaveChangesAsync();
        });

        var refreshed = await RefreshAsync(client, switched);

        Assert.False(refreshed.User.IsRoleSwitched);
        Assert.Null(refreshed.User.ActingOutletId);
        Assert.DoesNotContain(
            RoleSwitchClaims.ActingOutletId,
            new JwtSecurityTokenHandler().ReadJwtToken(refreshed.Token).Claims.Select(claim => claim.Type));
    }

    [Fact]
    public async Task Refresh_AfterSwitchingDisabled_FallsBack_AndUserCanStillReturn()
    {
        await SeedAsync();
        await AddSubjectAsync(ClerkRole, outlets: [Outlet1], mappedRoles: [CashierRole]);
        using var client = Client(SubjectUser, "Warehouse Clerk");
        var switched = await SwitchAsync(client, new RoleSwitchRequestDto { ActingRole = "Cashier" });

        await WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(user => user.Id == SubjectUser)).ContextSwitchEnabled = false;
            await db.SaveChangesAsync();
        });

        var refreshed = await RefreshAsync(client, switched);
        Assert.False(refreshed.User.IsRoleSwitched);
        Assert.Equal("Warehouse Clerk", refreshed.User.RoleName);

        client.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
        using var returning = await client.PostAsync("/api/auth/return-owner", content: null);
        Assert.Equal(HttpStatusCode.OK, returning.StatusCode);
    }

    private HttpClient Client(long userId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", BusinessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "accounts.view");
        return client;
    }

    private static async Task<UserContextOptionsDto> OptionsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/switch-options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserContextOptionsDto>())!;
    }

    private static async Task<LoginResponseDto> SwitchAsync(HttpClient client, RoleSwitchRequestDto request)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/switch-role", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponseDto>())!;
    }

    private static async Task<LoginResponseDto> RefreshAsync(HttpClient client, LoginResponseDto session)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequestDto
        {
            Token = session.Token,
            RefreshToken = session.RefreshToken
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponseDto>())!;
    }

    private async Task WithDbAsync(Func<RetailPOSDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>());
    }

    private Task AddSubjectAsync(
        long roleId,
        long? defaultOutlet = null,
        long[]? outlets = null,
        long[]? warehouses = null,
        long[]? mappedRoles = null,
        bool enabled = true,
        string locationScope = User.ContextLocationSelected,
        string roleScope = User.ContextRoleMapped) =>
        WithDbAsync(async db =>
        {
            db.Users.Add(new User
            {
                Id = SubjectUser,
                BusinessId = BusinessId,
                Name = "Subject",
                Email = "subject@example.test",
                PasswordHash = "not-used",
                RoleId = roleId,
                OutletId = defaultOutlet,
                PermissionOverrides = """["override.only"]""",
                ContextSwitchEnabled = enabled,
                ContextLocationScope = locationScope,
                ContextRoleScope = roleScope,
                IsActive = true
            });
            db.UserOutletAssignments.AddRange((outlets ?? []).Select(id => new UserOutletAssignment
            {
                UserId = SubjectUser,
                OutletId = id,
                BusinessId = BusinessId
            }));
            db.UserWarehouseAssignments.AddRange((warehouses ?? []).Select(id => new UserWarehouseAssignment
            {
                UserId = SubjectUser,
                WarehouseId = id,
                BusinessId = BusinessId
            }));
            db.UserContextRoles.AddRange((mappedRoles ?? []).Select(id => new UserContextRole
            {
                UserId = SubjectUser,
                RoleId = id,
                BusinessId = BusinessId
            }));
            await db.SaveChangesAsync();
        });

    private Task SeedAsync() =>
        WithDbAsync(async db =>
        {
            db.Businesses.AddRange(
                new Business { Id = BusinessId, Name = "Home", IsActive = true },
                new Business { Id = ForeignBusinessId, Name = "Foreign", IsActive = true });
            db.Roles.AddRange(
                new Role { Id = OwnerRole, Name = "BusinessOwner", Permissions = """["*"]""" },
                new Role { Id = ClerkRole, Name = "Warehouse Clerk", Permissions = """["inventory.view"]""" },
                new Role { Id = CashierRole, Name = "Cashier", Permissions = """["sales.create"]""" },
                new Role { Id = ForeignRole, Name = "Foreign Custom", Permissions = """["sales.view"]""" },
                new Role { Id = WildcardRole, Name = "Wildcard Custom", Permissions = """["*"]""" },
                new Role { Id = SuperAdminRole, Name = "Super Admin", Permissions = """["*"]""" });
            db.Outlets.AddRange(
                new Outlet { Id = Outlet1, BusinessId = BusinessId, Name = "Outlet One", Address = "A" },
                new Outlet { Id = Outlet2, BusinessId = BusinessId, Name = "Outlet Two", Address = "A" },
                new Outlet { Id = ForeignOutlet, BusinessId = ForeignBusinessId, Name = "Foreign Outlet", Address = "A" });
            db.Warehouses.AddRange(
                new Warehouse { Id = Warehouse1, BusinessId = BusinessId, Name = "Warehouse One", Address = "A" },
                new Warehouse { Id = Warehouse2, BusinessId = BusinessId, Name = "Warehouse Two", Address = "A" },
                new Warehouse { Id = ForeignWarehouse, BusinessId = ForeignBusinessId, Name = "Foreign Warehouse", Address = "A" });
            db.Users.AddRange(
                SeedUser(OwnerUser, BusinessId, OwnerRole),
                SeedUser(WildcardHolder, BusinessId, WildcardRole),
                SeedUser(ForeignHolder, ForeignBusinessId, ForeignRole),
                SeedUser(SuperAdminUser, null, SuperAdminRole));
            await db.SaveChangesAsync();
        });

    private static User SeedUser(long id, long? businessId, long roleId) => new()
    {
        Id = id,
        BusinessId = businessId,
        Name = $"User {id}",
        Email = $"user-{id}@example.test",
        PasswordHash = "not-used",
        RoleId = roleId,
        IsActive = true
    };
}
