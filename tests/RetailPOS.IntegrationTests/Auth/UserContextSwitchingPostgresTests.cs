using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RetailPOS.Application.Authentication.Context;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Authentication;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Identity;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Auth;

/// <summary>What the InMemory provider cannot prove: the migration, its constraints and the SQL of the role queries.</summary>
public sealed class UserContextSwitchingPostgresTests
{
    private const long Business = 96101, Foreign = 96102;
    private const long CashierRole = 96202, ForeignRole = 96203, LocalCustomRole = 96204, WildcardRole = 96205;
    private const long Owner = 96301, Subject = 96302, ForeignHolder = 96303, Legacy = 96304;
    private const long OutletId = 96401, ForeignOutletId = 96402, WarehouseId = 96501;

    [Fact]
    public async Task Migration_DefaultsExistingUsersToDisabled_AndRejectsInvalidScopes()
    {
        await RunAsync(async db =>
        {
            await SeedAsync(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO users (id, name, email, password_hash, permission_overrides, inventory_location_access_scope, is_active, must_reset_password, created_at, updated_at)
                VALUES ({Legacy}, 'Legacy', 'legacy-context@example.test', 'x', '[]'::jsonb, 'assigned_only', TRUE, FALSE, now(), now())
                """);

            var legacy = await db.Users.AsNoTracking().SingleAsync(user => user.Id == Legacy);
            Assert.False(legacy.ContextSwitchEnabled);
            Assert.Equal(User.ContextLocationSelected, legacy.ContextLocationScope);
            Assert.Equal(User.ContextRoleMapped, legacy.ContextRoleScope);

            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE users SET context_location_scope = 'everywhere' WHERE id = {Legacy}"));
            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE users SET context_role_scope = 'everything' WHERE id = {Legacy}"));
        });
    }

    [Fact]
    public async Task DuplicateRoleMapping_IsRejectedByTheDatabase()
    {
        await RunAsync(async db =>
        {
            await SeedAsync(db);
            db.UserContextRoles.Add(new UserContextRole { UserId = Subject, RoleId = CashierRole, BusinessId = Business });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            db.UserContextRoles.Add(new UserContextRole { UserId = Subject, RoleId = CashierRole, BusinessId = Business });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }

    [Fact]
    public async Task OwnerOptions_ExcludeForeignAndPrivilegedRolesAndLocations()
    {
        await RunAsync(async db =>
        {
            await SeedAsync(db);
            var store = new AuthenticationStore(db);
            var policy = new UserContextPolicy(store, new UserOutletAccessStore(db));

            var owner = await store.FindActiveUserForRoleSwitchAsync(Owner, CancellationToken.None);
            var options = await policy.GetOptionsAsync(owner, CancellationToken.None);

            var roleIds = options.Roles.Select(role => role.Id).ToArray();
            Assert.Contains(CashierRole, roleIds);
            Assert.Contains(LocalCustomRole, roleIds);
            Assert.DoesNotContain(ForeignRole, roleIds);
            Assert.DoesNotContain(options.Roles, role => role.Name == "BusinessOwner");
            Assert.DoesNotContain(WildcardRole, roleIds);
            Assert.Contains(options.Outlets, outlet => outlet.Id == OutletId);
            Assert.DoesNotContain(options.Outlets, outlet => outlet.Id == ForeignOutletId);
        });
    }

    [Fact]
    public async Task MappedRoles_AreReadOnlyInsideTheMappingsBusiness()
    {
        await RunAsync(async db =>
        {
            await SeedAsync(db);
            db.UserContextRoles.AddRange(
                new UserContextRole { UserId = Subject, RoleId = CashierRole, BusinessId = Business },
                new UserContextRole { UserId = Subject, RoleId = ForeignRole, BusinessId = Foreign });
            await db.SaveChangesAsync();
            var store = new AuthenticationStore(db);

            var mapped = await store.ListMappedContextRolesAsync(Subject, Business, CancellationToken.None);

            Assert.Equal([CashierRole], mapped.Select(role => role.Id));
        });
    }

    [Fact]
    public async Task SelectedLocationUser_ResolvesAssignedWarehouse_ThroughRealQueries()
    {
        await RunAsync(async db =>
        {
            await SeedAsync(db);
            var store = new AuthenticationStore(db);
            var policy = new UserContextPolicy(store, new UserOutletAccessStore(db));
            var subject = await store.FindActiveUserForRoleSwitchAsync(Subject, CancellationToken.None);

            var resolved = await policy.ResolveAsync(subject!, "Local Custom", null, null, CancellationToken.None);

            Assert.Equal(UserContextLocationTypes.Warehouse, resolved.LocationType);
            Assert.Equal(WarehouseId, resolved.WarehouseId);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                policy.ResolveAsync(subject!, "Local Custom", "outlet", ForeignOutletId, CancellationToken.None));
        });
    }

    private static async Task SeedAsync(RetailPOSDbContext db)
    {
        db.Businesses.AddRange(
            new Business { Id = Business, Name = "Home", IsActive = true },
            new Business { Id = Foreign, Name = "Foreign", IsActive = true });
        db.Roles.AddRange(
            new Role { Id = CashierRole, Name = "Stock Manager", Permissions = """["sales.create"]""" },
            new Role { Id = ForeignRole, Name = "Foreign Custom", Permissions = """["sales.view"]""" },
            new Role { Id = LocalCustomRole, Name = "Local Custom", Permissions = """["inventory.view"]""" },
            new Role { Id = WildcardRole, Name = "Wildcard Custom", Permissions = """["*"]""" });
        db.Outlets.AddRange(
            new Outlet { Id = OutletId, BusinessId = Business, Name = "Home Outlet", Address = "A" },
            new Outlet { Id = ForeignOutletId, BusinessId = Foreign, Name = "Foreign Outlet", Address = "A" });
        db.Warehouses.Add(new Warehouse { Id = WarehouseId, BusinessId = Business, Name = "Home Warehouse", Address = "A" });
        await db.SaveChangesAsync();

        var ownerRoleId = await db.Roles.AsNoTracking()
            .Where(role => role.Name == "BusinessOwner")
            .Select(role => (long?)role.Id)
            .FirstOrDefaultAsync();
        if (ownerRoleId is null)
        {
            var seeded = new Role { Id = 96206, Name = "BusinessOwner", Permissions = """["*"]""" };
            db.Roles.Add(seeded);
            await db.SaveChangesAsync();
            ownerRoleId = seeded.Id;
        }

        db.Users.AddRange(
            NewUser(Owner, Business, ownerRoleId.Value, enabled: false),
            NewUser(Subject, Business, LocalCustomRole, enabled: true),
            NewUser(ForeignHolder, Foreign, ForeignRole, enabled: false),
            NewUser(96305, Business, WildcardRole, enabled: false));
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment
        {
            UserId = Subject,
            WarehouseId = WarehouseId,
            BusinessId = Business
        });
        await db.SaveChangesAsync();
    }

    private static User NewUser(long id, long businessId, long roleId, bool enabled) => new()
    {
        Id = id,
        BusinessId = businessId,
        Name = $"User {id}",
        Email = $"context-{id}@example.test",
        PasswordHash = "not-used",
        RoleId = roleId,
        ContextSwitchEnabled = enabled,
        IsActive = true
    };

    private static Task RunAsync(Func<RetailPOSDbContext, Task> operation) =>
        PostgresTestDatabase.RunAsync(ResolveConnectionString(), PostgresTestDatabasePrefix.MigrationBaseline, async database =>
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(database.ConnectionString)
                .Options);
            await operation(db);
        });

    private static string ResolveConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder().AddJsonFile(settingsPath).Build().GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured for PostgreSQL tests.");
    }
}
