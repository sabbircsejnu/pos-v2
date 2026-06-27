using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.API.DTOs.Users;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Xunit.Abstractions;

namespace RetailPOS.Tests.Users;

public class SuperAdminUserUpdateHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;
    private readonly ITestOutputHelper _output;

    public SuperAdminUserUpdateHttpTests(RetailPosApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task SuperAdmin_CanUpdate_BusinessOwner_WithAllLocations_AndDefaultWarehouse()
    {
        const long businessId = 1001;
        const long roleId = 7001;
        const long userId = 9001;
        const long outletAId = 11001;
        const long outletBId = 11002;
        const long warehouseId = 12001;

        await SeedUserScenarioAsync(businessId, roleId, userId, outletAId, outletBId, warehouseId);

        using var client = CreateSuperAdminClient();

        var payload = new UpdateUserDto
        {
            Name = "Owner Updated",
            Email = "owner.updated@example.com",
            RoleId = roleId,
            BusinessId = businessId,
            OutletIds = new[] { outletAId, outletBId },
            WarehouseIds = new[] { warehouseId },
            DefaultLocationType = "warehouse",
            DefaultLocationId = warehouseId,
            InventoryLocationAccessScope = UpdateUserDto.ScopeAllLocations,
            IsActive = true
        };

        _output.WriteLine("Request payload: {0}", JsonSerializer.Serialize(payload));

        var response = await client.PutAsJsonAsync($"/api/users/{userId}", payload);
        var responseBody = await response.Content.ReadAsStringAsync();

        _output.WriteLine("Response status: {0}", (int)response.StatusCode);
        _output.WriteLine("Response body: {0}", responseBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        var user = await db.Users.FindAsync(userId);
        Assert.NotNull(user);
        Assert.Equal(businessId, user!.BusinessId);
        Assert.Equal("owner.updated@example.com", user.Email);
        Assert.Equal(User.InventoryAccessAll, user.InventoryLocationAccessScope);
        Assert.Null(user.OutletId);

        var outletAssignments = db.UserOutletAssignments.Where(a => a.UserId == userId).ToList();
        var warehouseAssignments = db.UserWarehouseAssignments.Where(a => a.UserId == userId).ToList();

        Assert.Empty(outletAssignments);
        Assert.Empty(warehouseAssignments);
    }

    [Fact]
    public async Task SuperAdmin_Update_WithCrossBusinessDefaultWarehouse_ReturnsExplicitValidationMessage()
    {
        const long businessA = 2001;
        const long businessB = 2002;
        const long roleId = 8001;
        const long userId = 9101;
        const long outletA = 21001;
        const long outletB = 21002;
        const long warehouseA = 22001;
        const long warehouseB = 22002;

        await SeedUserScenarioAsync(businessA, roleId, userId, outletA, outletB, warehouseA);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.Businesses.Add(new Business { Id = businessB, Name = "Business B", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.Warehouses.Add(new Warehouse { Id = warehouseB, Name = "Other Business Warehouse", Address = "B", BusinessId = businessB, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        using var client = CreateSuperAdminClient();

        var payload = new UpdateUserDto
        {
            Name = "Owner Updated",
            Email = "owner.updated@example.com",
            RoleId = roleId,
            BusinessId = businessA,
            DefaultLocationType = "warehouse",
            DefaultLocationId = warehouseB,
            InventoryLocationAccessScope = UpdateUserDto.ScopeAllLocations,
            IsActive = true
        };

        _output.WriteLine("Validation request payload: {0}", JsonSerializer.Serialize(payload));

        var response = await client.PutAsJsonAsync($"/api/users/{userId}", payload);
        var responseBody = await response.Content.ReadAsStringAsync();

        _output.WriteLine("Validation response status: {0}", (int)response.StatusCode);
        _output.WriteLine("Validation response body: {0}", responseBody);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("not in the user's business scope", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuperAdmin_Update_WithoutBusinessId_InfersBusinessFromDefaultWarehouse_WhenUserBusinessIsNull()
    {
        const long businessId = 3001;
        const long roleId = 9001;
        const long userId = 9201;
        const long outletAId = 31001;
        const long outletBId = 31002;
        const long warehouseId = 32001;

        await SeedUserScenarioAsync(businessId, roleId, userId, outletAId, outletBId, warehouseId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var targetUser = await db.Users.FirstAsync(u => u.Id == userId);
            targetUser.BusinessId = null;
            await db.SaveChangesAsync();
        }

        using var client = CreateSuperAdminClient();

        var payload = new UpdateUserDto
        {
            Name = "Owner Inferred Business",
            Email = "owner.inferred@example.com",
            RoleId = roleId,
            DefaultLocationType = "warehouse",
            DefaultLocationId = warehouseId,
            InventoryLocationAccessScope = UpdateUserDto.ScopeAllLocations,
            IsActive = true
        };

        _output.WriteLine("Inference request payload: {0}", JsonSerializer.Serialize(payload));

        var response = await client.PutAsJsonAsync($"/api/users/{userId}", payload);
        var responseBody = await response.Content.ReadAsStringAsync();

        _output.WriteLine("Inference response status: {0}", (int)response.StatusCode);
        _output.WriteLine("Inference response body: {0}", responseBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var document = JsonDocument.Parse(responseBody))
        {
            var root = document.RootElement;
            Assert.Equal(businessId, root.GetProperty("businessId").GetInt64());
            Assert.Equal("all_locations", root.GetProperty("inventoryLocationAccessScope").GetString());
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var updatedUser = await verifyDb.Users.FirstAsync(u => u.Id == userId);
        Assert.Equal(businessId, updatedUser.BusinessId);
    }

    private HttpClient CreateSuperAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "9999");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "999");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "users.view,users.edit,*");
        return client;
    }

    private async Task SeedUserScenarioAsync(
        long businessId,
        long roleId,
        long userId,
        long outletAId,
        long outletBId,
        long warehouseId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

        if (!db.Businesses.Any(b => b.Id == businessId))
        {
            db.Businesses.Add(new Business
            {
                Id = businessId,
                Name = $"Business {businessId}",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!db.Roles.Any(r => r.Id == roleId))
        {
            db.Roles.Add(new Role
            {
                Id = roleId,
                Name = "BusinessOwner",
                Permissions = "[\"*\"]",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!db.Outlets.Any(o => o.Id == outletAId))
        {
            db.Outlets.Add(new Outlet
            {
                Id = outletAId,
                Name = "Outlet A",
                Address = "A",
                BusinessId = businessId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!db.Outlets.Any(o => o.Id == outletBId))
        {
            db.Outlets.Add(new Outlet
            {
                Id = outletBId,
                Name = "Outlet B",
                Address = "B",
                BusinessId = businessId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!db.Warehouses.Any(w => w.Id == warehouseId))
        {
            db.Warehouses.Add(new Warehouse
            {
                Id = warehouseId,
                Name = "Main Warehouse",
                Address = "HQ",
                BusinessId = businessId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        var existingUser = db.Users.FirstOrDefault(u => u.Id == userId);
        if (existingUser != null)
        {
            db.Users.Remove(existingUser);
        }

        db.Users.Add(new User
        {
            Id = userId,
            BusinessId = businessId,
            Name = "Owner Original",
            Email = "owner.original@example.com",
            PasswordHash = "hash",
            RoleId = roleId,
            OutletId = outletAId,
            InventoryLocationAccessScope = User.InventoryAccessSpecific,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }
}
