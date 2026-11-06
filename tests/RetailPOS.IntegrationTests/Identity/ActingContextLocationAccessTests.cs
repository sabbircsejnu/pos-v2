using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Identity;

namespace RetailPOS.Tests.Identity;

/// <summary>Runtime enforcement of a switched context: tokens are signed, but may be stale.</summary>
public sealed class ActingContextLocationAccessTests
{
    private const long Business = 1, UserId = 10, Outlet1 = 101, Outlet2 = 102, Warehouse1 = 201, Warehouse2 = 202;

    [Fact]
    public async Task AssignedActingOutlet_IsUsedForCheckout_WhenNoOutletIsRequested()
    {
        await using var db = await SeedAsync(enabled: true, outletAssignments: [Outlet1, Outlet2]);
        var access = new UserOutletAccess(new UserOutletAccessStore(db));

        Assert.Equal(Outlet2, await access.EnforceWriteOutletAsync(Switched(outlet: Outlet2), null));
    }

    [Fact]
    public async Task RemovedAssignment_StopsTheActingOutletImmediately_WithoutWaitingForTokenExpiry()
    {
        await using var db = await SeedAsync(enabled: true, outletAssignments: [Outlet1]);
        var access = new UserOutletAccess(new UserOutletAccessStore(db));

        // The token still names outlet 2, which is no longer assigned.
        var context = Switched(outlet: Outlet2);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.EnforceWriteOutletAsync(context, Outlet2));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.EnforceWriteOutletAsync(context, null));
    }

    [Fact]
    public async Task DisabledSwitching_StopsTheActingOutletImmediately()
    {
        await using var db = await SeedAsync(enabled: false, outletAssignments: [Outlet1, Outlet2]);
        var access = new UserOutletAccess(new UserOutletAccessStore(db));

        var authorized = await access.GetAuthorizedOutletsAsync(Switched(outlet: Outlet2));

        Assert.DoesNotContain(authorized.Outlets, outlet => outlet.Id == Outlet2);
    }

    [Theory]
    [InlineData(Warehouse1)]
    [InlineData(null)]
    public async Task WarehouseOrAllLocationsContext_IsNotACheckoutLocation(long? warehouse)
    {
        await using var db = await SeedAsync(enabled: true, outletAssignments: [Outlet1], warehouseAssignments: [Warehouse1]);
        var access = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = Switched(warehouse: warehouse);

        await Assert.ThrowsAsync<InvalidOperationException>(() => access.EnforceWriteOutletAsync(context, null));
    }

    [Fact]
    public async Task WarehouseOnlyUser_HasNoCheckoutOutlet_ButKeepsWarehouseOperations()
    {
        await using var db = await SeedAsync(enabled: true, warehouseAssignments: [Warehouse1]);
        var access = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = Switched(warehouse: Warehouse1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.EnforceWriteOutletAsync(context, Outlet1));
        var location = await access.EnforceWriteLocationAsync(context, Warehouse1, "warehouse");

        Assert.Equal((Warehouse1, "warehouse"), location);
    }

    [Fact]
    public async Task AllLocationsUser_CanWorkAtTheSwitchedLocation_ButNotOthers()
    {
        await using var db = await SeedAsync(enabled: true, locationScope: User.ContextLocationAll);
        var access = new UserOutletAccess(new UserOutletAccessStore(db));

        Assert.Equal(Outlet2, await access.EnforceWriteOutletAsync(Switched(outlet: Outlet2), Outlet2));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => access.EnforceWriteOutletAsync(Switched(outlet: Outlet2), Outlet1));
    }

    [Fact]
    public async Task AllLocationsContext_DoesNotWidenStockCountOrTransferSource()
    {
        await using var db = await SeedAsync(enabled: true, locationScope: User.ContextLocationAll, warehouseAssignments: [Warehouse1]);
        var access = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = Switched(warehouse: Warehouse2);

        foreach (var policy in new[] { LocationAccessPolicy.StockCountOperation, LocationAccessPolicy.StockTransferSource })
        {
            var locations = await access.GetAuthorizedLocationsAsync(context, policy);

            Assert.Equal([Warehouse1], locations.Warehouses.Select(warehouse => warehouse.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => access.EnforceWriteLocationAsync(context, policy, Warehouse2, "warehouse"));
        }
    }

    [Fact]
    public async Task ForeignOutletInToken_IsIgnored_EvenWithAllLocations()
    {
        await using var db = await SeedAsync(enabled: true, locationScope: User.ContextLocationAll);
        var access = new UserOutletAccess(new UserOutletAccessStore(db));

        var authorized = await access.GetAuthorizedOutletsAsync(Switched(outlet: 999));

        Assert.DoesNotContain(authorized.Outlets, outlet => outlet.Id == 999);
    }

    [Fact]
    public async Task UnswitchedSession_IsUnchanged()
    {
        await using var db = await SeedAsync(enabled: false, defaultOutlet: Outlet1);
        var access = new UserOutletAccess(new UserOutletAccessStore(db));
        var context = new UserOutletAccessContext(UserId, false, false, Business, Outlet1);

        Assert.Equal(Outlet1, await access.EnforceWriteOutletAsync(context, null));
    }

    private static UserOutletAccessContext Switched(long? outlet = null, long? warehouse = null) =>
        new(UserId, false, false, Business, outlet, EffectiveWarehouseId: warehouse, IsContextSwitched: true);

    private static async Task<RetailPOSDbContext> SeedAsync(
        bool enabled,
        long? defaultOutlet = null,
        string locationScope = User.ContextLocationSelected,
        long[]? outletAssignments = null,
        long[]? warehouseAssignments = null)
    {
        var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase($"acting-context-{Guid.NewGuid()}")
            .Options);
        db.Users.Add(new User
        {
            Id = UserId,
            BusinessId = Business,
            OutletId = defaultOutlet,
            InventoryLocationAccessScope = User.InventoryAccessAssignedOnly,
            ContextSwitchEnabled = enabled,
            ContextLocationScope = locationScope
        });
        db.Outlets.AddRange(
            new Outlet { Id = Outlet1, BusinessId = Business, Name = "One" },
            new Outlet { Id = Outlet2, BusinessId = Business, Name = "Two" },
            new Outlet { Id = 999, BusinessId = 2, Name = "Foreign" });
        db.Warehouses.AddRange(
            new Warehouse { Id = Warehouse1, BusinessId = Business, Name = "W1" },
            new Warehouse { Id = Warehouse2, BusinessId = Business, Name = "W2" });
        db.UserOutletAssignments.AddRange((outletAssignments ?? []).Select(id => new UserOutletAssignment
        {
            UserId = UserId,
            OutletId = id,
            BusinessId = Business
        }));
        db.UserWarehouseAssignments.AddRange((warehouseAssignments ?? []).Select(id => new UserWarehouseAssignment
        {
            UserId = UserId,
            WarehouseId = id,
            BusinessId = Business
        }));
        await db.SaveChangesAsync();
        return db;
    }
}
