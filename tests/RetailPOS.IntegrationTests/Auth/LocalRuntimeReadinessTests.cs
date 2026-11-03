using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Auth;

public sealed class LocalRuntimeReadinessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingDemoSeed_RepairsOnlyIdentifiedAssignmentsAndIsIdempotent(bool relational)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>();
        if (relational)
            options.UseSqlite(connection);
        else
            options.UseInMemoryDatabase(Guid.NewGuid().ToString());
        await using var db = new RetailPOSDbContext(options.Options);
        await db.Database.EnsureCreatedAsync();
        var business = new Business { Name = "RetailPOS Demo Business", IsActive = true };
        var foreign = new Business { Name = "Real Business", IsActive = true };
        db.Businesses.AddRange(business, foreign);
        var ownerRole = new Role { Name = "BusinessOwner", Permissions = "[\"*\"]" };
        var managerRole = new Role { Name = "Manager", Permissions = "[]" };
        var cashierRole = new Role { Name = "Cashier", Permissions = "[\"sales.create\",\"inventory.view\"]" };
        var stockRole = new Role { Name = "Stock Manager", Permissions = "[]" };
        db.Roles.AddRange(ownerRole, managerRole, cashierRole, stockRole);
        await db.SaveChangesAsync();
        var owner = new User { Name = "Business Owner", Email = "owner@retailpos.com", RoleId = ownerRole.Id, PasswordHash = "unused" };
        var manager = new User { Name = "Sarah Manager", Email = "sarah.manager@retailpos.com", RoleId = managerRole.Id, PasswordHash = "unused" };
        var cashier = new User { Name = "Mike Cashier", Email = "mike.cashier@retailpos.com", RoleId = cashierRole.Id, PasswordHash = "unused" };
        var stock = new User { Name = "Tom Stock Manager", Email = "tom.stock@retailpos.com", RoleId = stockRole.Id, PasswordHash = "unused" };
        var real = new User { Name = "Real Owner", Email = "real@example.test", BusinessId = foreign.Id, RoleId = ownerRole.Id, PasswordHash = "unused" };
        db.Users.AddRange(owner, manager, cashier, stock, real);
        await db.SaveChangesAsync();
        var outlet = new Outlet { Name = "Main Store", Address = "123 Main Street, New York, NY 10001", ManagerId = manager.Id };
        var warehouse = new Warehouse { Name = "Central Warehouse", Address = "789 Warehouse Blvd, Brooklyn, NY 11201", ManagerId = stock.Id };
        var supplier = new Supplier { Name = "TechSupply Co.", Contact = "contact@techsupply.com" };
        var unrelated = new Supplier { Name = "Real Supplier", Contact = "real@example.test" };
        db.Outlets.Add(outlet);
        db.Warehouses.Add(warehouse);
        db.Suppliers.AddRange(supplier, unrelated);
        await db.SaveChangesAsync();

        await DbSeeder.SeedAsync(db);
        Assert.All(new[] { owner, manager, cashier, stock }, user => Assert.Equal(business.Id, user.BusinessId));
        Assert.Equal(business.Id, outlet.BusinessId);
        Assert.Equal(business.Id, warehouse.BusinessId);
        Assert.Equal(business.Id, supplier.BusinessId);
        Assert.Null(unrelated.BusinessId);
        Assert.Equal(foreign.Id, real.BusinessId);
        var permissions = System.Text.Json.JsonSerializer.Deserialize<string[]>(cashierRole.Permissions)!;
        Assert.Contains("categories.view", permissions);
        Assert.DoesNotContain("settings.view", permissions);
        Assert.DoesNotContain("settings.edit", permissions);
        Assert.DoesNotContain("*", permissions);
        var roleCount = await db.Roles.CountAsync();
        var userCount = await db.Users.CountAsync();
        var terminalCount = await db.PosTerminals.CountAsync();
        var permissionJson = cashierRole.Permissions;
        await DbSeeder.SeedAsync(db);
        Assert.Equal(roleCount, await db.Roles.CountAsync());
        Assert.Equal(userCount, await db.Users.CountAsync());
        Assert.Equal(terminalCount, await db.PosTerminals.CountAsync());
        Assert.Equal(permissionJson, cashierRole.Permissions);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.State == EntityState.Modified);
    }

    [Fact]
    public async Task FreshDemoSeed_AssignsBusinessWithoutAssigningPlatformSuperAdmin()
    {
        await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DbSeeder.SeedAsync(db);
        var demo = await db.Businesses.SingleAsync();
        Assert.All(await db.Users.Include(user => user.Role).Where(user => user.Role!.Name != "Super Admin")
            .ToListAsync(), user => Assert.Equal(demo.Id, user.BusinessId));
        Assert.Null((await db.Users.Include(user => user.Role)
            .SingleAsync(user => user.Role!.Name == "Super Admin")).BusinessId);
        Assert.All(await db.Outlets.ToListAsync(), outlet => Assert.Equal(demo.Id, outlet.BusinessId));
        Assert.All(await db.Warehouses.ToListAsync(), warehouse => Assert.Equal(demo.Id, warehouse.BusinessId));
        Assert.All(await db.Suppliers.ToListAsync(), supplier => Assert.Equal(demo.Id, supplier.BusinessId));
    }

    [Fact]
    public async Task DemoRepair_RejectsExistingForeignAssignmentWithoutReassigningIt()
    {
        await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Businesses.Add(new Business { Name = "RetailPOS Demo Business" });
        var role = new Role { Name = "BusinessOwner", Permissions = "[]" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        var owner = new User { Name = "Owner", Email = "owner@retailpos.com", RoleId = role.Id, BusinessId = 991, PasswordHash = "unused" };
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DbSeeder.SeedAsync(db));
        Assert.Equal(991, owner.BusinessId);
    }

    [Fact]
    public async Task SuperAdminContext_RequiresPersistedActiveRoleAndBusiness_AndMeExposesSelection()
    {
        using var factory = new RetailPosApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Businesses.AddRange(new Business { Id = 781, Name = "Selected", IsActive = true },
            new Business { Id = 782, Name = "Inactive", IsActive = false });
        var role = new Role { Id = 781, Name = "Super Admin", Permissions = "[\"*\"]" };
        db.Roles.Add(role);
        db.Users.Add(new User { Id = 781, Name = "Admin", Email = "admin@example.test", RoleId = role.Id, IsActive = true, PasswordHash = "unused" });
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "781");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "*");
        var selection = await client.PostAsJsonAsync("/api/Auth/business-context", new { businessId = 781 });
        Assert.Equal(HttpStatusCode.OK, selection.StatusCode);
        client.DefaultRequestHeaders.Add("X-Business-Id", "781");
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/Auth/me");
        Assert.Equal(781, me.GetProperty("businessId").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/accounting/reports/trial-balance")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Business-Id");
        client.DefaultRequestHeaders.Add("X-Business-Id", "782");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/Auth/me")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Business-Id");
        client.DefaultRequestHeaders.Add("X-Business-Id", "999999");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/Auth/me")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Business-Id");
        client.DefaultRequestHeaders.Add("X-Business-Id", "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/Auth/me")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Business-Id");
        role.Name = "BusinessOwner";
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/Auth/business-context", new { businessId = 781 })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Business-Id", "781");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/Auth/me")).StatusCode);
    }

    [Fact]
    public async Task CashierPosReads_ArePermittedWithoutSettingsOrAccountingPrivileges()
    {
        using var factory = new RetailPosApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Cashier");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "products.view,categories.view,inventory.view,sales.create");
        foreach (var route in new[] { "/api/categories", "/api/settings/tax", "/api/settings/receipt", "/api/settings/inventory", "/api/settings/currency", "/api/Products/variants/search" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/accounting/reports/trial-balance")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/settings/tax", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/Auth/business-context", new { businessId = 1 })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Business-Id", "2");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/categories")).StatusCode);
    }
}
