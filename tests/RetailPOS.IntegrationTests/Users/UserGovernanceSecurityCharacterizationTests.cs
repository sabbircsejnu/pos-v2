using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Users;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Users;

public sealed class UserGovernanceSecurityCharacterizationTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private const long BusinessId = 81001;
    private const long OtherBusinessId = 81005;
    private const long OrdinaryRoleId = 81002;
    private const long SuperAdminRoleId = 81003;
    private const long ActorUserId = 81004;

    public UserGovernanceSecurityCharacterizationTests(RetailPosApiFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task OrdinaryUser_CannotCreateUserWithProtectedSuperAdminRole()
    {
        await SeedAsync();
        using var client = CreateClient("users.view,users.create");

        using var response = await client.PostAsJsonAsync("/api/users", new CreateUserDto
        {
            Name = "Unauthorized Super Admin",
            Email = "unauthorized-super-admin@example.test",
            Password = "test-password",
            RoleId = SuperAdminRoleId,
            InventoryLocationAccessScope = User.InventoryAccessAll,
            BusinessId = OtherBusinessId,
            UserPermissions = ["users.create", "users.edit"],
            IsActive = true
        });

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Null(await verifyDb.Users.SingleOrDefaultAsync(
            user => user.Email == "unauthorized-super-admin@example.test"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await verifyDb.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task PersistedSuperAdmin_CannotDemoteItsOwnFinalActiveRole()
    {
        await SeedWithActorAsync(SuperAdminRoleId, null);
        using var client = CreateClient("users.view,users.edit", actorRole: "Super Admin");

        using var response = await client.PutAsJsonAsync($"/api/users/{ActorUserId}", NewUpdateDto(
            "self-demotion@example.test",
            OrdinaryRoleId,
            BusinessId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var actor = await verifyDb.Users.SingleAsync(user => user.Id == ActorUserId);
        Assert.Equal(SuperAdminRoleId, actor.RoleId);
        Assert.True(actor.IsActive);
        Assert.Empty(await verifyDb.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task EffectiveSwitchedSuperAdmin_DoesNotAuthorizeProtectedRoleAssignment()
    {
        await SeedAsync();
        using var client = CreateClient("users.view,users.create", effectiveRole: "Super Admin", isRoleSwitched: true);

        using var response = await client.PostAsJsonAsync("/api/users", NewCreateDto(
            "switched-super-admin@example.test",
            SuperAdminRoleId,
            BusinessId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertUserAbsentAsync("switched-super-admin@example.test");
    }

    [Fact]
    public async Task OrdinaryUser_CannotPromoteOrMutateProtectedSuperAdmin()
    {
        const long targetUserId = 81010;
        const long promotionTargetId = 81014;
        await SeedAsync(
            new User
            {
                Id = targetUserId,
                BusinessId = BusinessId,
                Name = "Protected Super Admin",
                Email = "protected-super-admin@example.test",
                PasswordHash = "not-used",
                RoleId = SuperAdminRoleId,
                IsActive = true
            },
            new User
            {
                Id = promotionTargetId,
                BusinessId = BusinessId,
                Name = "Ordinary User",
                Email = "ordinary-user@example.test",
                PasswordHash = "not-used",
                RoleId = OrdinaryRoleId,
                IsActive = true
            });
        using var client = CreateClient("users.view,users.edit,users.delete");

        using var promote = await client.PutAsJsonAsync($"/api/users/{promotionTargetId}", NewUpdateDto(
            "promoted-user@example.test",
            SuperAdminRoleId,
            BusinessId));
        using var update = await client.PutAsJsonAsync($"/api/users/{targetUserId}", NewUpdateDto(
            "changed-super-admin@example.test",
            SuperAdminRoleId,
            BusinessId));
        using var deactivate = await client.PutAsync($"/api/users/{targetUserId}/deactivate", null);
        using var delete = await client.DeleteAsync($"/api/users/{targetUserId}");

        Assert.Equal(HttpStatusCode.Forbidden, promote.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var target = await verifyDb.Users.SingleAsync(user => user.Id == targetUserId);
        Assert.Equal(SuperAdminRoleId, target.RoleId);
        Assert.True(target.IsActive);
        Assert.Empty(await verifyDb.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task PersistedSuperAdmin_CanAssignProtectedRole()
    {
        await SeedWithActorAsync(SuperAdminRoleId, null);
        using var client = CreateClient("users.view,users.create", actorRole: "Super Admin");

        using var response = await client.PostAsJsonAsync("/api/users", NewCreateDto(
            "authorized-super-admin@example.test",
            SuperAdminRoleId,
            BusinessId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var created = await verifyDb.Users.SingleAsync(user => user.Email == "authorized-super-admin@example.test");
        Assert.Equal(SuperAdminRoleId, created.RoleId);
    }

    [Fact]
    public async Task PersistedSuperAdmin_CanManageAnotherSuperAdmin()
    {
        const long targetUserId = 81011;
        await SeedWithActorAsync(
            SuperAdminRoleId,
            null,
            new User
            {
                Id = targetUserId,
                BusinessId = BusinessId,
                Name = "Other Super Admin",
                Email = "other-super-admin@example.test",
                PasswordHash = "not-used",
                RoleId = SuperAdminRoleId,
                IsActive = true
            });
        using var client = CreateClient("users.view,users.edit", actorRole: "Super Admin");

        using var response = await client.PutAsJsonAsync($"/api/users/{targetUserId}", NewUpdateDto(
            "updated-super-admin@example.test",
            OrdinaryRoleId,
            BusinessId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var target = await verifyDb.Users.SingleAsync(user => user.Id == targetUserId);
        Assert.Equal("updated-super-admin@example.test", target.Email);
        Assert.Equal(OrdinaryRoleId, target.RoleId);
        var audit = await verifyDb.AuditEvents
            .Include(eventRecord => eventRecord.Entities)
                .ThenInclude(entity => entity.FieldChanges)
            .SingleAsync(eventRecord =>
                eventRecord.ActionSummary == $"User access updated: {targetUserId}");
        Assert.Equal(ActorUserId, audit.RealUserId);
        Assert.Equal("PermissionChange", audit.ActionType);
        Assert.True(audit.CreatedAt > DateTime.MinValue);
        var fields = audit.Entities.Single().FieldChanges.ToDictionary(change => change.FieldName);
        Assert.Equal(SuperAdminRoleId.ToString(), fields["RoleId"].OldValue);
        Assert.Equal(OrdinaryRoleId.ToString(), fields["RoleId"].NewValue);
        Assert.Contains("BusinessId", fields.Keys);
        Assert.Contains("OutletId", fields.Keys);
        Assert.Contains("PermissionOverrides", fields.Keys);
    }

    [Fact]
    public async Task PersistedSuperAdmin_CanDeleteUserInAnotherBusiness()
    {
        const long targetUserId = 81012;
        await SeedWithActorAsync(
            SuperAdminRoleId,
            BusinessId,
            new User
            {
                Id = targetUserId,
                BusinessId = OtherBusinessId,
                Name = "Other Business User",
                Email = "other-business-user@example.test",
                PasswordHash = "not-used",
                RoleId = OrdinaryRoleId,
                IsActive = true
            });
        using var client = CreateClient("users.view,users.delete", actorRole: "Super Admin");

        using var response = await client.DeleteAsync($"/api/users/{targetUserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var target = await verifyDb.Users.SingleAsync(user => user.Id == targetUserId);
        Assert.False(target.IsActive);
        var audit = await verifyDb.AuditEvents.SingleAsync(eventRecord =>
            eventRecord.ActionSummary == $"User deactivated by delete: {targetUserId}");
        Assert.Equal(ActorUserId, audit.RealUserId);
    }

    [Fact]
    public async Task User_CannotDeactivateOrDeleteSelf()
    {
        await SeedAsync();
        using var client = CreateClient("users.view,users.edit,users.delete");

        using var deactivate = await client.PutAsync($"/api/users/{ActorUserId}/deactivate", null);
        using var delete = await client.DeleteAsync($"/api/users/{ActorUserId}");

        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.True((await verifyDb.Users.SingleAsync(user => user.Id == ActorUserId)).IsActive);
        Assert.Empty(await verifyDb.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task OrdinaryUser_CannotTargetAnotherBusinessOrAssignItsOutlet()
    {
        const long otherUserId = 81012;
        const long otherOutletId = 81013;
        await SeedAsync(
            new User
            {
                Id = otherUserId,
                BusinessId = OtherBusinessId,
                Name = "Other Business User",
                Email = "other-business-user@example.test",
                PasswordHash = "not-used",
                RoleId = OrdinaryRoleId,
                IsActive = true
            },
            new Outlet
            {
                Id = otherOutletId,
                BusinessId = OtherBusinessId,
                Name = "Other Business Outlet"
            });
        using var client = CreateClient("users.view,users.edit,users.create");

        using var update = await client.PutAsJsonAsync($"/api/users/{otherUserId}", NewUpdateDto(
            "changed-other-business@example.test",
            OrdinaryRoleId,
            BusinessId));
        using var create = await client.PostAsJsonAsync("/api/users", new CreateUserDto
        {
            Name = "Cross-business outlet attempt",
            Email = "cross-business-outlet@example.test",
            Password = "test-password",
            RoleId = OrdinaryRoleId,
            BusinessId = OtherBusinessId,
            OutletId = otherOutletId,
            InventoryLocationAccessScope = User.InventoryAccessAll
        });

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        await AssertUserAbsentAsync("cross-business-outlet@example.test");
    }

    private Task SeedAsync(params object[] additionalEntities) =>
        SeedWithActorAsync(OrdinaryRoleId, BusinessId, additionalEntities);

    private async Task SeedWithActorAsync(
        long actorRoleId,
        long? actorBusinessId,
        params object[] additionalEntities)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(
            new Business { Id = BusinessId, Name = "Governance Security Business", IsActive = true },
            new Business { Id = OtherBusinessId, Name = "Other Governance Business", IsActive = true });
        db.Roles.AddRange(
            new Role { Id = OrdinaryRoleId, Name = "AccountsAdmin", Permissions = """["users.view","users.create","users.edit","users.delete"]""" },
            new Role { Id = SuperAdminRoleId, Name = "Super Admin", Permissions = """["*"]""" });
        db.Users.Add(new User
        {
            Id = ActorUserId,
            BusinessId = actorBusinessId,
            Name = "Governance Test Actor",
            Email = "governance-actor@example.test",
            PasswordHash = "not-used",
            RoleId = actorRoleId,
            IsActive = true
        });
        db.AddRange(additionalEntities);
        await db.SaveChangesAsync();
    }

    private HttpClient CreateClient(
        string permissions,
        string actorRole = "AccountsAdmin",
        string? effectiveRole = null,
        bool isRoleSwitched = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", BusinessId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-UserId", ActorUserId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", actorRole);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (effectiveRole is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-EffectiveRole", effectiveRole);
        }

        if (isRoleSwitched)
        {
            client.DefaultRequestHeaders.Add("X-Test-RoleSwitched", "true");
        }

        return client;
    }

    private async Task AssertUserAbsentAsync(string email)
    {
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Null(await verifyDb.Users.SingleOrDefaultAsync(user => user.Email == email));
    }

    private static CreateUserDto NewCreateDto(string email, long roleId, long businessId) => new()
    {
        Name = "Governance User",
        Email = email,
        Password = "test-password",
        RoleId = roleId,
        BusinessId = businessId,
        InventoryLocationAccessScope = User.InventoryAccessAll,
        IsActive = true
    };

    private static UpdateUserDto NewUpdateDto(string email, long roleId, long businessId) => new()
    {
        Name = "Updated Governance User",
        Email = email,
        RoleId = roleId,
        BusinessId = businessId,
        InventoryLocationAccessScope = User.InventoryAccessAll,
        IsActive = true
    };
}
