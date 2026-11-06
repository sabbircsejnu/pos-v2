using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Identity;

namespace RetailPOS.Tests.Identity;

public sealed class UserOutletAccessBehaviorTests
{
    [Fact]
    public async Task SpecificLocationAccess_FiltersBusinessMembershipAndEnforcesRequestedScope()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Id = 7701,
            BusinessId = 1,
            InventoryLocationAccessScope = User.InventoryAccessSpecific
        });
        db.Outlets.AddRange(
            new Outlet { Id = 7001, BusinessId = 1, Name = "Primary outlet" },
            new Outlet { Id = 7002, BusinessId = 1, Name = "Second outlet" },
            new Outlet { Id = 7003, BusinessId = 2, Name = "Foreign outlet" },
            new Outlet { Id = 7004, BusinessId = 1, Name = "Unassigned outlet" });
        db.Warehouses.AddRange(
            new Warehouse { Id = 8001, BusinessId = 1, Name = "Assigned warehouse" },
            new Warehouse { Id = 8002, BusinessId = 2, Name = "Foreign warehouse" });
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { Id = 1, UserId = 7701, OutletId = 7001, BusinessId = 1, IsPrimary = true },
            new UserOutletAssignment { Id = 2, UserId = 7701, OutletId = 7002, BusinessId = 1 },
            new UserOutletAssignment { Id = 3, UserId = 7701, OutletId = 7003, BusinessId = 2 },
            new UserOutletAssignment { Id = 4, UserId = 7701, OutletId = 7004, BusinessId = 1, IsActive = false });
        db.UserWarehouseAssignments.AddRange(
            new UserWarehouseAssignment { Id = 1, UserId = 7701, WarehouseId = 8001, BusinessId = 1, IsPrimary = true },
            new UserWarehouseAssignment { Id = 2, UserId = 7701, WarehouseId = 8002, BusinessId = 2 });
        await db.SaveChangesAsync();

        var service = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = new UserOutletAccessContext(7701, false, false, 1, null);
        var access = await service.GetAuthorizedOutletsAsync(context);

        Assert.Equal([7001L, 7002L], access.Outlets.Select(outlet => outlet.Id));
        Assert.Equal([8001L], access.Warehouses.Select(warehouse => warehouse.Id));
        Assert.Equal([7001L, 7002L, 7004L], access.DestinationOutlets.Select(outlet => outlet.Id));
        Assert.Equal([8001L], access.DestinationWarehouses.Select(warehouse => warehouse.Id));
        Assert.Equal(7001, access.DefaultLocationId);
        Assert.False(access.IsGlobalAccess);

        var resolvedMultipleOutlets = await service.ResolveAndAuthorizeLocationAsync(context, null, "outlet");
        Assert.Null(resolvedMultipleOutlets.LocationId);
        Assert.Equal("outlet", resolvedMultipleOutlets.LocationType);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.ResolveAndAuthorizeLocationAsync(context, 7003, "outlet"));
        // Several authorized outlets and no default: the caller must choose; no outlet is guessed.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnforceWriteOutletAsync(context, null));
        Assert.Equal(7002, await service.EnforceWriteOutletAsync(context, 7002));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteOutletAsync(context, 7004));
        Assert.Equal(
            (8001L, "warehouse"),
            await service.EnforceWriteLocationAsync(context, 8001, "WAREHOUSE"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteLocationAsync(context, 8002, "warehouse"));
    }

    [Theory]
    [InlineData(User.InventoryAccessAssignedOnly)]
    [InlineData(User.InventoryAccessSpecific)]
    public async Task WarehouseOnlyUser_CanOperateOnAssignedWarehouse_ButHasNoOutletContext(string scope)
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 7801, BusinessId = 1, InventoryLocationAccessScope = scope });
        db.Outlets.Add(new Outlet { Id = 7101, BusinessId = 1, Name = "Outlet" });
        db.Warehouses.AddRange(
            new Warehouse { Id = 8101, BusinessId = 1, Name = "Assigned warehouse" },
            new Warehouse { Id = 8102, BusinessId = 2, Name = "Foreign warehouse" });
        db.UserWarehouseAssignments.Add(
            new UserWarehouseAssignment { Id = 11, UserId = 7801, WarehouseId = 8101, BusinessId = 1, IsPrimary = true });
        await db.SaveChangesAsync();

        var service = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = new UserOutletAccessContext(7801, false, false, 1, null);

        Assert.Equal(
            (8101L, "warehouse"),
            await service.EnforceWriteLocationAsync(context, LocationAccessPolicy.StockTransferSource, 8101, "warehouse"));
        Assert.Equal(
            (8101L, "warehouse"),
            await service.EnforceWriteLocationAsync(context, LocationAccessPolicy.StockCountOperation, 8101, "warehouse"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteLocationAsync(context, 8102, "warehouse"));

        // POS cash session and checkout resolve through this gate: no authorized outlet, no operation.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteOutletAsync(context, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteOutletAsync(context, 7101));
    }

    [Fact]
    public async Task EnforceWriteOutlet_WithoutRequest_UsesAuthorizedDefaultOrOnlyOutlet()
    {
        await using var db = CreateDbContext();
        db.Users.AddRange(
            new User { Id = 7811, BusinessId = 1, OutletId = 7202, InventoryLocationAccessScope = User.InventoryAccessSpecific },
            new User { Id = 7812, BusinessId = 1, InventoryLocationAccessScope = User.InventoryAccessSpecific });
        db.Outlets.AddRange(
            new Outlet { Id = 7201, BusinessId = 1, Name = "A outlet" },
            new Outlet { Id = 7202, BusinessId = 1, Name = "B outlet" });
        db.UserOutletAssignments.AddRange(
            new UserOutletAssignment { Id = 21, UserId = 7811, OutletId = 7201, BusinessId = 1, IsPrimary = true },
            new UserOutletAssignment { Id = 22, UserId = 7811, OutletId = 7202, BusinessId = 1 },
            new UserOutletAssignment { Id = 23, UserId = 7812, OutletId = 7201, BusinessId = 1, IsPrimary = true });
        await db.SaveChangesAsync();

        var service = new UserOutletAccess(new UserOutletAccessStore(db));

        Assert.Equal(7202, await service.EnforceWriteOutletAsync(new UserOutletAccessContext(7811, false, false, 1, null), null));
        Assert.Equal(7201, await service.EnforceWriteOutletAsync(new UserOutletAccessContext(7812, false, false, 1, null), null));
    }

    [Fact]
    public async Task EnforceWriteOutlet_WithForeignStoredDefault_DoesNotAuthorizeIt()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 7821, BusinessId = 1, OutletId = 7301, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly });
        db.Outlets.Add(new Outlet { Id = 7301, BusinessId = 2, Name = "Foreign outlet" });
        await db.SaveChangesAsync();

        var service = new UserOutletAccess(new UserOutletAccessStore(db));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteOutletAsync(new UserOutletAccessContext(7821, false, false, 1, null), null));
    }

    [Fact]
    public async Task LegacyBusinessOwnerAccess_RemainsBusinessScopedAndGlobalWithinBusiness()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Id = 7702,
            BusinessId = 1,
            OutletId = 7001,
            InventoryLocationAccessScope = string.Empty
        });
        db.Outlets.AddRange(
            new Outlet { Id = 7001, BusinessId = 1, Name = "Owner outlet" },
            new Outlet { Id = 7002, BusinessId = 1, Name = "Second outlet" },
            new Outlet { Id = 7003, BusinessId = 2, Name = "Foreign outlet" });
        db.Warehouses.AddRange(
            new Warehouse { Id = 8001, BusinessId = 1, Name = "Owner warehouse" },
            new Warehouse { Id = 8002, BusinessId = 2, Name = "Foreign warehouse" });
        await db.SaveChangesAsync();

        var service = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = new UserOutletAccessContext(7702, false, true, 1, null);
        var access = await service.GetAuthorizedOutletsAsync(context);

        Assert.Equal([7001L, 7002L], access.Outlets.Select(outlet => outlet.Id));
        Assert.Equal([8001L], access.Warehouses.Select(warehouse => warehouse.Id));
        Assert.True(access.IsBusinessOwner);
        Assert.True(access.IsGlobalAccess);
        Assert.Equal(User.InventoryAccessAll, access.AccessScope);
        Assert.Equal(7001, access.DefaultOutletId);
        Assert.Equal(
            (7002L, "outlet"),
            await service.EnforceWriteLocationAsync(context, 7002, "OUTLET"));
    }

    [Fact]
    public async Task SuperAdminAccess_IsScopedWhenBusinessContextIsSupplied()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 7703 });
        db.Outlets.AddRange(
            new Outlet { Id = 7001, BusinessId = 1, Name = "Business one" },
            new Outlet { Id = 7002, BusinessId = 2, Name = "Business two" });
        await db.SaveChangesAsync();

        var service = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = new UserOutletAccessContext(7703, true, false, 1, 7002);
        var access = await service.GetAuthorizedOutletsAsync(context);

        Assert.Equal([7001L], access.Outlets.Select(outlet => outlet.Id));
        Assert.True(access.IsGlobalAccess);
        Assert.Null(access.DefaultOutletId);
    }

    [Fact]
    public async Task SuperAdminAccess_WithoutBusinessContext_IsRefusedInsteadOfListingEveryBusiness()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 7707 });
        db.Outlets.AddRange(
            new Outlet { Id = 7001, BusinessId = 1, Name = "Business one" },
            new Outlet { Id = 7002, BusinessId = 2, Name = "Business two" });
        await db.SaveChangesAsync();
        var service = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = new UserOutletAccessContext(7707, true, false, null, null);

        await Assert.ThrowsAsync<BusinessContextRequiredException>(() => service.GetAuthorizedOutletsAsync(context));
        await Assert.ThrowsAsync<BusinessContextRequiredException>(
            () => service.GetAuthorizedLocationsAsync(context, LocationAccessPolicy.InventoryStockView));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task WildcardOwner_WithDefaultAssignedScope_ListsAndAuthorizesBusinessLocations(
        bool withOutlet, bool withWarehouse)
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 7704, BusinessId = 1 });
        db.Outlets.Add(new Outlet { Id = 7002, BusinessId = 2, Name = "Foreign outlet" });
        db.Warehouses.Add(new Warehouse { Id = 8002, BusinessId = 2, Name = "Foreign warehouse" });
        if (withOutlet)
            db.Outlets.Add(new Outlet { Id = 7001, BusinessId = 1, Name = "Business outlet" });
        if (withWarehouse)
            db.Warehouses.Add(new Warehouse { Id = 8001, BusinessId = 1, Name = "Business warehouse" });
        await db.SaveChangesAsync();

        var service = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = new UserOutletAccessContext(7704, false, true, 1, null, HasWildcardPermission: true);
        var access = await service.GetAuthorizedOutletsAsync(context);

        Assert.True(access.IsGlobalAccess);
        Assert.Equal(User.InventoryAccessAll, access.AccessScope);
        Assert.Equal(withOutlet ? 1 : 0, access.Outlets.Count);
        Assert.Equal(withWarehouse ? 1 : 0, access.Warehouses.Count);
        if (withOutlet)
            Assert.Equal((7001L, "outlet"), await service.EnforceWriteLocationAsync(context, 7001, "outlet"));
        if (withWarehouse)
            Assert.Equal((8001L, "warehouse"), await service.EnforceWriteLocationAsync(context, 8001, "warehouse"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteLocationAsync(context, 7002, "outlet"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteLocationAsync(context, 8002, "warehouse"));
    }

    [Fact]
    public async Task GlobalAccess_UsesSelectedBusinessAndDoesNotKeepForeignDefault()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 7705, BusinessId = 1, OutletId = 7001 });
        db.Outlets.AddRange(
            new Outlet { Id = 7001, BusinessId = 1, Name = "Home outlet" },
            new Outlet { Id = 7002, BusinessId = 2, Name = "Selected outlet" });
        await db.SaveChangesAsync();
        var service = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = new UserOutletAccessContext(7705, false, true, 2, 7001, HasWildcardPermission: true);

        var access = await service.GetAuthorizedOutletsAsync(context);

        Assert.Equal(7002, Assert.Single(access.Outlets).Id);
        Assert.Null(access.DefaultOutletId);
        Assert.Equal(7002, access.DefaultLocationId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteLocationAsync(context, 7001, "outlet"));

        var homeAccess = await service.GetAuthorizedOutletsAsync(context with { EffectiveBusinessId = 1 });
        Assert.Equal(7001, Assert.Single(homeAccess.Outlets).Id);
    }

    [Fact]
    public async Task WildcardOwner_WithoutBusinessContext_DoesNotListAllBusinesses()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 7706 });
        await db.SaveChangesAsync();
        var service = new UserOutletAccess(new UserOutletAccessStore(db));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAuthorizedOutletsAsync(
            new UserOutletAccessContext(7706, false, true, null, null, HasWildcardPermission: true)));
    }

    private static RetailPOSDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

}
