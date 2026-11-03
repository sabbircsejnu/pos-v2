using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Identity;

namespace RetailPOS.Tests.Identity;

public sealed class LocationAccessPolicyBehaviorTests
{
    private const long Business = 1;
    private const long OtherBusiness = 2;

    // Business 1: outlets 101 (assigned), 102 (assigned, inactive), 103 (unassigned), warehouses 201 (assigned), 202 (unassigned).
    // Business 2: outlet 191 and warehouse 291, both (wrongly) assigned to the users to prove tenant isolation.
    private const long AllScopeUser = 9001;
    private const long AssignedOnlyUser = 9002;
    private const long NoAssignmentUser = 9003;
    private const long OwnerUser = 9004;
    private const long SuperAdminUser = 9005;
    private const long NoBusinessUser = 9006;

    public static TheoryData<LocationAccessPolicy> Step5Policies => new()
    {
        LocationAccessPolicy.StockCountOperation,
        LocationAccessPolicy.StockTransferSource
    };

    [Fact]
    public async Task InventoryStockView_FollowsStep3Scope_AndMatchesLegacyBehavior()
    {
        var (service, _) = await CreateAsync();
        var allScope = Context(AllScopeUser);
        var assignedOnly = Context(AssignedOnlyUser, outletId: null);

        var allView = await service.GetAuthorizedLocationsAsync(allScope, LocationAccessPolicy.InventoryStockView);
        var legacyAll = await service.GetAuthorizedOutletsAsync(allScope);
        var assignedView = await service.GetAuthorizedLocationsAsync(assignedOnly, LocationAccessPolicy.InventoryStockView);

        Assert.Equal([101L, 102L, 103L], allView.Outlets.Select(l => l.Id));
        Assert.Equal([201L, 202L], allView.Warehouses.Select(l => l.Id));
        Assert.Equal(legacyAll.Outlets, allView.Outlets);
        Assert.Equal(LocationAccessPolicy.InventoryStockView, allView.Policy);
        Assert.Equal([103L], assignedView.Outlets.Select(l => l.Id));
    }

    [Theory]
    [MemberData(nameof(Step5Policies))]
    public async Task Step5Policies_UseOnlyActiveSameBusinessAssignments_RegardlessOfStep3(LocationAccessPolicy policy)
    {
        var (service, _) = await CreateAsync();

        var allScope = await service.GetAuthorizedLocationsAsync(Context(AllScopeUser), policy);
        var assignedOnly = await service.GetAuthorizedLocationsAsync(Context(AssignedOnlyUser), policy);

        // Step 3 = all_locations does not widen it; inactive and cross-business assignments are ignored.
        Assert.Equal([101L], allScope.Outlets.Select(l => l.Id));
        Assert.Equal([201L], allScope.Warehouses.Select(l => l.Id));
        Assert.False(allScope.IsGlobalAccess);
        Assert.Equal(policy, allScope.Policy);
        // The Step 3 default outlet (103) is not an assignment, so it is not selectable.
        Assert.Equal([101L], assignedOnly.Outlets.Select(l => l.Id));
        Assert.DoesNotContain(allScope.Outlets, l => l.Id is 102 or 103 or 191);
        Assert.DoesNotContain(allScope.Warehouses, l => l.Id is 202 or 291);
    }

    [Theory]
    [MemberData(nameof(Step5Policies))]
    public async Task Step5Policies_RejectUnassignedLocationsForReadAndWrite(LocationAccessPolicy policy)
    {
        var (service, _) = await CreateAsync();
        var context = Context(AllScopeUser);

        Assert.Equal((101L, "outlet"), await service.ResolveAndAuthorizeLocationAsync(context, policy, 101, "outlet"));
        Assert.Equal((201L, "warehouse"), await service.EnforceWriteLocationAsync(context, policy, 201, "WAREHOUSE"));
        foreach (var (id, type) in new[] { (103L, "outlet"), (102L, "outlet"), (191L, "outlet"), (202L, "warehouse"), (291L, "warehouse") })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => service.ResolveAndAuthorizeLocationAsync(context, policy, id, type));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => service.EnforceWriteLocationAsync(context, policy, id, type));
        }

        // The same user may still *view* stock everywhere because InventoryStockView follows Step 3.
        Assert.Equal(
            (103L, "outlet"),
            await service.ResolveAndAuthorizeLocationAsync(context, LocationAccessPolicy.InventoryStockView, 103, "outlet"));
    }

    [Theory]
    [MemberData(nameof(Step5Policies))]
    public async Task Step5Policies_UserWithoutAssignments_HasNoLocationsAndIsUnauthorized(LocationAccessPolicy policy)
    {
        var (service, _) = await CreateAsync();
        var context = Context(NoAssignmentUser);

        var access = await service.GetAuthorizedLocationsAsync(context, policy);

        Assert.Empty(access.Outlets);
        Assert.Empty(access.Warehouses);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.ResolveAndAuthorizeLocationAsync(context, policy, null, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteLocationAsync(context, policy, 101, "outlet"));
    }

    [Fact]
    public async Task StockTransferDestination_IsEveryBusinessLocation_RegardlessOfStep3AndStep5()
    {
        var (service, _) = await CreateAsync();

        foreach (var userId in new[] { AllScopeUser, AssignedOnlyUser, NoAssignmentUser })
        {
            var access = await service.GetAuthorizedLocationsAsync(
                Context(userId), LocationAccessPolicy.StockTransferDestination);

            Assert.Equal([101L, 102L, 103L], access.Outlets.Select(l => l.Id));
            Assert.Equal([201L, 202L], access.Warehouses.Select(l => l.Id));
            Assert.DoesNotContain(access.Outlets, l => l.Id == 191);
            Assert.DoesNotContain(access.Warehouses, l => l.Id == 291);
            Assert.Equal(LocationAccessPolicy.StockTransferDestination, access.Policy);
        }

        var context = Context(NoAssignmentUser);
        Assert.Equal(
            (103L, "outlet"),
            await service.ResolveAndAuthorizeLocationAsync(
                context, LocationAccessPolicy.StockTransferDestination, 103, "outlet"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ResolveAndAuthorizeLocationAsync(
            context, LocationAccessPolicy.StockTransferDestination, 191, "outlet"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ResolveAndAuthorizeLocationAsync(
            context, LocationAccessPolicy.StockTransferDestination, 291, "warehouse"));
    }

    [Fact]
    public async Task TransferSourceAndDestination_AreSeparate_ForTheSameUser()
    {
        var (service, _) = await CreateAsync();
        var context = Context(AllScopeUser);

        var source = await service.GetAuthorizedLocationsAsync(context, LocationAccessPolicy.StockTransferSource);
        var destination = await service.GetAuthorizedLocationsAsync(context, LocationAccessPolicy.StockTransferDestination);

        Assert.Equal([101L], source.Outlets.Select(l => l.Id));
        Assert.Equal([101L, 102L, 103L], destination.Outlets.Select(l => l.Id));
        // Source responses still expose the business-wide destination lookup for the transfer form.
        Assert.Equal([101L, 102L, 103L], source.DestinationOutlets.Select(l => l.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.EnforceWriteLocationAsync(context, LocationAccessPolicy.StockTransferSource, 103, "outlet"));
    }

    [Theory]
    [InlineData(OwnerUser, true, false)]
    [InlineData(SuperAdminUser, false, true)]
    public async Task PrivilegedRoles_KeepBusinessWideAccessUnderEveryStep5AndDestinationPolicy(
        long userId,
        bool isOwner,
        bool isSuperAdmin)
    {
        var (service, _) = await CreateAsync();
        // No wildcard permission: Step 5 policies must not narrow a Business Owner / Super Admin.
        var context = new UserOutletAccessContext(userId, isSuperAdmin, isOwner, Business, null);

        foreach (var policy in Enum.GetValues<LocationAccessPolicy>().Where(p => p != LocationAccessPolicy.InventoryStockView))
        {
            var access = await service.GetAuthorizedLocationsAsync(context, policy);
            Assert.Equal(policy, access.Policy);
            Assert.Contains(access.Outlets, l => l.Id == 103);
            Assert.Contains(access.Warehouses, l => l.Id == 202);
            Assert.DoesNotContain(access.Outlets, l => l.Id == 191);
            Assert.DoesNotContain(access.Warehouses, l => l.Id == 291);
            Assert.Equal(
                (103L, "outlet"),
                await service.EnforceWriteLocationAsync(context, policy, 103, "outlet"));
        }

        var view = await service.GetAuthorizedLocationsAsync(
            context with { HasWildcardPermission = true },
            LocationAccessPolicy.InventoryStockView);
        Assert.Contains(view.Outlets, l => l.Id == 103);
    }

    [Fact]
    public async Task TenantIsolation_BusinessContextIsRequired_AndCannotBeBypassedWithAnotherBusinessLocation()
    {
        var (service, _) = await CreateAsync();

        foreach (var policy in Enum.GetValues<LocationAccessPolicy>().Where(p => p != LocationAccessPolicy.InventoryStockView))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAuthorizedLocationsAsync(
                new UserOutletAccessContext(NoBusinessUser, false, false, null, null), policy));
        }

        var otherBusiness = new UserOutletAccessContext(AllScopeUser, false, false, OtherBusiness, null);
        var destination = await service.GetAuthorizedLocationsAsync(otherBusiness, LocationAccessPolicy.StockTransferDestination);
        Assert.Equal([191L], destination.Outlets.Select(l => l.Id));
        Assert.Equal([291L], destination.Warehouses.Select(l => l.Id));
    }

    [Fact]
    public async Task UnauthenticatedOrUnknownUser_AndUnknownPolicy_AreRejected()
    {
        var (service, _) = await CreateAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAuthorizedLocationsAsync(
            new UserOutletAccessContext(null, false, false, Business, null), LocationAccessPolicy.StockCountOperation));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAuthorizedLocationsAsync(
            Context(123456), LocationAccessPolicy.StockTransferDestination));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetAuthorizedLocationsAsync(
            Context(AllScopeUser), (LocationAccessPolicy)99));
    }

    [Theory]
    [InlineData("StockCountOperation", true, LocationAccessPolicy.StockCountOperation)]
    [InlineData("stocktransfersource", true, LocationAccessPolicy.StockTransferSource)]
    [InlineData(" StockTransferDestination ", true, LocationAccessPolicy.StockTransferDestination)]
    [InlineData("InventoryStockView", true, LocationAccessPolicy.InventoryStockView)]
    [InlineData("Everything", false, LocationAccessPolicy.InventoryStockView)]
    [InlineData("42", false, LocationAccessPolicy.InventoryStockView)]
    [InlineData("", false, LocationAccessPolicy.InventoryStockView)]
    public void PolicyNames_ParseStrictly(string value, bool expected, LocationAccessPolicy policy)
    {
        Assert.Equal(expected, LocationAccessPolicies.TryParse(value, out var parsed));
        Assert.Equal(policy, parsed);
    }

    private static UserOutletAccessContext Context(long userId, long? outletId = null) =>
        new(userId, false, false, Business, outletId);

    private static async Task<(UserOutletAccess Service, RetailPOSDbContext Db)> CreateAsync()
    {
        var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Outlets.AddRange(
            new Outlet { Id = 101, BusinessId = Business, Name = "Assigned outlet" },
            new Outlet { Id = 102, BusinessId = Business, Name = "Inactive assignment outlet" },
            new Outlet { Id = 103, BusinessId = Business, Name = "Unassigned outlet" },
            new Outlet { Id = 191, BusinessId = OtherBusiness, Name = "Foreign outlet" });
        db.Warehouses.AddRange(
            new Warehouse { Id = 201, BusinessId = Business, Name = "Assigned warehouse" },
            new Warehouse { Id = 202, BusinessId = Business, Name = "Unassigned warehouse" },
            new Warehouse { Id = 291, BusinessId = OtherBusiness, Name = "Foreign warehouse" });
        db.Users.AddRange(
            new User { Id = AllScopeUser, BusinessId = Business, OutletId = 103, InventoryLocationAccessScope = User.InventoryAccessAll },
            new User { Id = AssignedOnlyUser, BusinessId = Business, OutletId = 103, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly },
            new User { Id = NoAssignmentUser, BusinessId = Business, OutletId = 101, InventoryLocationAccessScope = User.InventoryAccessSpecific },
            new User { Id = OwnerUser, BusinessId = Business, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly },
            new User { Id = SuperAdminUser, BusinessId = Business, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly },
            new User { Id = NoBusinessUser, BusinessId = null, InventoryLocationAccessScope = User.InventoryAccessAssignedOnly });
        foreach (var userId in new[] { AllScopeUser, AssignedOnlyUser })
        {
            db.UserOutletAssignments.AddRange(
                new UserOutletAssignment { UserId = userId, OutletId = 101, BusinessId = Business, IsPrimary = true },
                new UserOutletAssignment { UserId = userId, OutletId = 102, BusinessId = Business, IsActive = false },
                new UserOutletAssignment { UserId = userId, OutletId = 191, BusinessId = OtherBusiness });
            db.UserWarehouseAssignments.AddRange(
                new UserWarehouseAssignment { UserId = userId, WarehouseId = 201, BusinessId = Business, IsPrimary = true },
                new UserWarehouseAssignment { UserId = userId, WarehouseId = 291, BusinessId = OtherBusiness });
        }

        await db.SaveChangesAsync();
        return (new UserOutletAccess(new UserOutletAccessStore(db)), db);
    }
}
