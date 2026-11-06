using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Users;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Users;

public sealed class UserContextAccessGovernanceHttpTests : IDisposable
{
    private const long BusinessId = 9601;
    private const long ForeignBusinessId = 9602;
    private const long ClerkRole = 9701, CashierRole = 9702, OwnerRole = 9703, ForeignRole = 9704, ManagerRole = 9705, BroadRole = 9706;
    private const long Warehouse = 9801;
    private const long Target = 9901, Actor = 9902, ForeignHolder = 9903, BroadHolder = 9904, OwnerUser = 9905;
    private const string Manage = PermissionCatalog.UserContextAccessManage;
    private const string GrantAll = PermissionCatalog.UserContextAccessGrantAll;

    private readonly RetailPosApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task UsersEditAlone_CannotChangeContextAccess()
    {
        await SeedAsync();
        using var client = Client(Actor, "users.edit,sales.create");

        using var response = await UpdateAsync(client, Target, Access(enabled: true, roleIds: [CashierRole]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False((await FindUserAsync(Target)).ContextSwitchEnabled);
    }

    [Fact]
    public async Task UsersEditAlone_CanStillEditUser_WhenContextAccessIsOmitted()
    {
        await SeedAsync();
        await SetTargetAccessAsync(enabled: true, roleIds: [CashierRole]);
        using var client = Client(Actor, "users.edit");

        using var response = await UpdateAsync(client, Target, access: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await FindUserAsync(Target)).ContextSwitchEnabled);
        var mapped = await MappedRolesAsync(Target);
        Assert.Equal(new[] { CashierRole }, mapped);
    }

    [Theory]
    [InlineData(User.ContextLocationAll, User.ContextRoleMapped)]
    [InlineData(User.ContextLocationSelected, User.ContextRoleAll)]
    public async Task ManagePermission_CannotGrantAllScope_WithoutGrantAll(string locationScope, string roleScope)
    {
        await SeedAsync();
        using var client = Client(Actor, $"users.edit,{Manage}");

        using var response = await UpdateAsync(client, Target, Access(true, locationScope, roleScope));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GrantAllHolder_CanGrantAllScopes_PersistedAndAudited()
    {
        await SeedAsync();
        using var client = Client(Actor, $"users.edit,{Manage},{GrantAll}");

        using var response = await UpdateAsync(
            client,
            Target,
            Access(true, User.ContextLocationAll, User.ContextRoleAll));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.True(dto.ContextAccess!.Enabled);
        Assert.Equal(User.ContextLocationAll, dto.ContextAccess.LocationScope);
        Assert.Equal(User.ContextRoleAll, dto.ContextAccess.RoleScope);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var auditFields = await db.AuditEventFieldChanges
            .Where(change => change.FieldName == "ContextRoleScope")
            .ToListAsync();
        Assert.NotEmpty(auditFields);
    }

    [Fact]
    public async Task Create_WithContextAccess_RequiresManagePermission_AndPersistsMapping()
    {
        await SeedAsync();
        var dto = new CreateUserDto
        {
            Name = "New User",
            Email = "new-context-user@example.test",
            Password = "secret123",
            RoleId = ClerkRole,
            WarehouseIds = [Warehouse],
            InventoryLocationAccessScope = CreateUserDto.ScopeAssignedOnly,
            IsActive = true,
            ContextAccess = Access(enabled: true, roleIds: [CashierRole])
        };

        using var denied = Client(Actor, "users.create,sales.create");
        using var deniedResponse = await denied.PostAsJsonAsync("/api/users", dto);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var allowed = Client(Actor, $"users.create,{Manage},sales.create");
        using var response = await allowed.PostAsJsonAsync("/api/users", dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal([CashierRole], created.ContextAccess!.RoleIds);
        Assert.Null(created.OutletId);
    }

    [Fact]
    public async Task CannotChangeOwnContextAccess_EvenWithGrantAll()
    {
        await SeedAsync();
        using var client = Client(Actor, $"users.edit,{Manage},{GrantAll}");

        using var response = await UpdateAsync(
            client,
            Actor,
            Access(true, User.ContextLocationAll, User.ContextRoleAll),
            roleId: ManagerRole);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CannotMapRoleWithPermissionsTheActorDoesNotHold()
    {
        await SeedAsync();
        using var client = Client(Actor, $"users.edit,{Manage},sales.create");

        using var response = await UpdateAsync(client, Target, Access(enabled: true, roleIds: [BroadRole]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(OwnerRole)]
    [InlineData(ForeignRole)]
    [InlineData(987654)]
    public async Task CannotMapPrivilegedForeignOrUnknownRole_EvenAsFullyPrivilegedActor(long roleId)
    {
        await SeedAsync();
        using var client = Client(OwnerUser, "*", "BusinessOwner");

        using var response = await UpdateAsync(client, Target, Access(enabled: true, roleIds: [roleId]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BusinessOwner_CanGrantAllScopesAndMapRoles_ThroughWildcard()
    {
        await SeedAsync();
        using var client = Client(OwnerUser, "*", "BusinessOwner");

        using var response = await UpdateAsync(
            client,
            Target,
            Access(true, User.ContextLocationAll, User.ContextRoleMapped, [CashierRole, BroadRole]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mapped = await MappedRolesAsync(Target);
        Assert.Equal(new[] { CashierRole, BroadRole }, mapped);
    }

    [Fact]
    public async Task ReplacingMappedRoles_RemovesTheOldOnes()
    {
        await SeedAsync();
        await SetTargetAccessAsync(enabled: true, roleIds: [CashierRole, BroadRole]);
        using var client = Client(OwnerUser, "*", "BusinessOwner");

        using var response = await UpdateAsync(client, Target, Access(enabled: true, roleIds: [BroadRole]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mapped = await MappedRolesAsync(Target);
        Assert.Equal(new[] { BroadRole }, mapped);
    }

    [Fact]
    public async Task InvalidScopeValue_IsRejected()
    {
        await SeedAsync();
        using var client = Client(OwnerUser, "*", "BusinessOwner");

        using var response = await UpdateAsync(client, Target, Access(true, "everywhere", User.ContextRoleMapped));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(Manage)]
    [InlineData(GrantAll)]
    public async Task UsersEditAlone_CannotHandOutGuardedKeysAsPerUserOverride(string key)
    {
        await SeedAsync();
        using var client = Client(Actor, "users.edit");

        using var response = await UpdateAsync(client, Target, access: null, userPermissions: [key]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CannotSelfGrantGuardedKeyAsPerUserOverride_EvenWhenHoldingIt()
    {
        await SeedAsync();
        using var client = Client(Actor, $"users.edit,{Manage}");

        using var response = await UpdateAsync(client, Actor, access: null, roleId: ManagerRole, userPermissions: [Manage]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ContextRoleOptions_RequireManagePermission_AndExcludePrivilegedAndForeign()
    {
        await SeedAsync();
        using var denied = Client(Actor, "users.edit");
        using var deniedResponse = await denied.GetAsync("/api/users/context-role-options");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var allowed = Client(Actor, Manage);
        using var response = await allowed.GetAsync("/api/users/context-role-options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var names = (await response.Content.ReadFromJsonAsync<UserContextRoleOptionDto[]>())!
            .Select(role => role.Name)
            .ToArray();

        Assert.Contains("Cashier", names);
        Assert.Contains("Warehouse Clerk", names);
        Assert.DoesNotContain("BusinessOwner", names);
        Assert.DoesNotContain("Foreign Custom", names);
    }

    private static UserContextAccessDto Access(
        bool enabled,
        string locationScope = User.ContextLocationSelected,
        string roleScope = User.ContextRoleMapped,
        long[]? roleIds = null) =>
        new()
        {
            Enabled = enabled,
            LocationScope = locationScope,
            RoleScope = roleScope,
            RoleIds = roleIds ?? []
        };

    private static Task<HttpResponseMessage> UpdateAsync(
        HttpClient client,
        long userId,
        UserContextAccessDto? access,
        long roleId = ClerkRole,
        string[]? userPermissions = null) =>
        client.PutAsJsonAsync($"/api/users/{userId}", new UpdateUserDto
        {
            Name = $"User {userId}",
            Email = $"user-{userId}@example.test",
            RoleId = roleId,
            WarehouseIds = [Warehouse],
            InventoryLocationAccessScope = UpdateUserDto.ScopeAssignedOnly,
            UserPermissions = userPermissions,
            IsActive = true,
            ContextAccess = access
        });

    private HttpClient Client(long userId, string permissions, string role = "Context Manager")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", BusinessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Permissions", $"users.view,{permissions}");
        return client;
    }

    private async Task<User> FindUserAsync(long id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
            .Users.AsNoTracking().SingleAsync(user => user.Id == id);
    }

    private async Task<long[]> MappedRolesAsync(long id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
            .UserContextRoles.AsNoTracking()
            .Where(mapping => mapping.UserId == id)
            .OrderBy(mapping => mapping.RoleId)
            .Select(mapping => mapping.RoleId)
            .ToArrayAsync();
    }

    private async Task SetTargetAccessAsync(bool enabled, long[] roleIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        (await db.Users.SingleAsync(user => user.Id == Target)).ContextSwitchEnabled = enabled;
        db.UserContextRoles.AddRange(roleIds.Select(id => new UserContextRole
        {
            UserId = Target,
            RoleId = id,
            BusinessId = BusinessId
        }));
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Businesses.AddRange(
            new Business { Id = BusinessId, Name = "Home", IsActive = true },
            new Business { Id = ForeignBusinessId, Name = "Foreign", IsActive = true });
        db.Roles.AddRange(
            new Role { Id = ClerkRole, Name = "Warehouse Clerk", Permissions = """["inventory.view"]""" },
            new Role { Id = CashierRole, Name = "Cashier", Permissions = """["sales.create"]""" },
            new Role { Id = OwnerRole, Name = "BusinessOwner", Permissions = """["*"]""" },
            new Role { Id = ForeignRole, Name = "Foreign Custom", Permissions = """["sales.view"]""" },
            new Role { Id = ManagerRole, Name = "Context Manager", Permissions = """["users.edit"]""" },
            new Role { Id = BroadRole, Name = "Broad Custom", Permissions = """["sales.create","sales.void"]""" });
        db.Warehouses.Add(new Warehouse { Id = Warehouse, BusinessId = BusinessId, Name = "Main", Address = "A" });
        db.Users.AddRange(
            SeedUser(Target, BusinessId, ClerkRole),
            SeedUser(Actor, BusinessId, ManagerRole),
            SeedUser(BroadHolder, BusinessId, BroadRole),
            SeedUser(ForeignHolder, ForeignBusinessId, ForeignRole),
            SeedUser(OwnerUser, BusinessId, OwnerRole));
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment
        {
            UserId = Target,
            WarehouseId = Warehouse,
            BusinessId = BusinessId,
            IsPrimary = true
        });
        await db.SaveChangesAsync();
    }

    private static User SeedUser(long id, long businessId, long roleId) => new()
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
