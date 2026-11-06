using Moq;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Grns;

public sealed class GrnViewLocationPolicyTests
{
    private const long UserId = 77;
    private const long BusinessId = 1;
    private const long AssignedOutlet = 201;
    private const long DefaultOutlet = 202;
    private const long AssignedWarehouse = 301;

    private static readonly IReadOnlyList<AuthorizedLocationReadModel> BusinessOutlets =
    [
        new(AssignedOutlet, "Assigned outlet", "outlet"),
        new(DefaultOutlet, "Default outlet", "outlet")
    ];

    private static readonly IReadOnlyList<AuthorizedLocationReadModel> BusinessWarehouses =
    [
        new(AssignedWarehouse, "Assigned warehouse", "warehouse"),
        new(302, "Other warehouse", "warehouse")
    ];

    [Fact]
    public async Task BusinessOwner_SeesEveryLocationWithoutAnyAssignment()
    {
        var access = CreateAccess(new UserLocationAccessUser(BusinessId, null, User.InventoryAccessAssignedOnly));

        var result = await access.GetAuthorizedLocationsAsync(
            new UserOutletAccessContext(UserId, false, true, BusinessId, null),
            LocationAccessPolicy.GrnView);

        Assert.True(result.IsGlobalAccess);
        Assert.Equal(BusinessOutlets, result.Outlets);
        Assert.Equal(BusinessWarehouses, result.Warehouses);
    }

    [Fact]
    public async Task SuperAdmin_SeesEveryLocationOfTheExplicitlySelectedBusiness()
    {
        var access = CreateAccess(new UserLocationAccessUser(null, null, null));

        var result = await access.GetAuthorizedLocationsAsync(
            new UserOutletAccessContext(UserId, true, false, BusinessId, null, HasWildcardPermission: true),
            LocationAccessPolicy.GrnView);

        Assert.True(result.IsGlobalAccess);
        Assert.Equal(BusinessOutlets, result.Outlets);
        Assert.Equal(BusinessWarehouses, result.Warehouses);
    }

    [Fact]
    public async Task SuperAdmin_WithoutASelectedBusiness_IsRefused()
    {
        var access = CreateAccess(new UserLocationAccessUser(null, null, null));

        await Assert.ThrowsAsync<BusinessContextRequiredException>(() => access.GetAuthorizedLocationsAsync(
            new UserOutletAccessContext(UserId, true, false, null, null, HasWildcardPermission: true),
            LocationAccessPolicy.GrnView));
    }

    [Theory]
    [InlineData(User.InventoryAccessAssignedOnly)]
    [InlineData(User.InventoryAccessSpecific)]
    [InlineData(User.InventoryAccessAll)]
    public async Task OrdinaryUser_IsLimitedToStep5_WhateverTheInventoryScopeAndDefaultOutlet(string inventoryScope)
    {
        var access = CreateAccess(
            new UserLocationAccessUser(BusinessId, DefaultOutlet, inventoryScope),
            outletAssignments: [Outlet(AssignedOutlet)],
            warehouseAssignments: [Warehouse(AssignedWarehouse)]);

        var result = await access.GetAuthorizedLocationsAsync(
            new UserOutletAccessContext(UserId, false, false, BusinessId, DefaultOutlet),
            LocationAccessPolicy.GrnView);

        Assert.False(result.IsGlobalAccess);
        Assert.Equal([AssignedOutlet], result.Outlets.Select(location => location.Id));
        Assert.Equal([AssignedWarehouse], result.Warehouses.Select(location => location.Id));
    }

    [Fact]
    public async Task WarehouseOnlyUser_WorksWithoutADefaultOutlet()
    {
        var access = CreateAccess(
            new UserLocationAccessUser(BusinessId, null, User.InventoryAccessAssignedOnly),
            warehouseAssignments: [Warehouse(AssignedWarehouse)]);

        var result = await access.GetAuthorizedLocationsAsync(
            new UserOutletAccessContext(UserId, false, false, BusinessId, null),
            LocationAccessPolicy.GrnView);

        Assert.Empty(result.Outlets);
        Assert.Equal([AssignedWarehouse], result.Warehouses.Select(location => location.Id));
    }

    [Fact]
    public async Task UserWithoutAssignments_GetsAnEmptyScopeRatherThanAnError()
    {
        var access = CreateAccess(new UserLocationAccessUser(BusinessId, DefaultOutlet, User.InventoryAccessAll));

        var result = await access.GetAuthorizedLocationsAsync(
            new UserOutletAccessContext(UserId, false, false, BusinessId, null),
            LocationAccessPolicy.GrnView);

        Assert.False(result.IsGlobalAccess);
        Assert.Empty(result.Outlets);
        Assert.Empty(result.Warehouses);
    }

    [Fact]
    public async Task SwitchingLocation_DoesNotExpandAssignments()
    {
        var access = CreateAccess(
            new UserLocationAccessUser(BusinessId, null, User.InventoryAccessAssignedOnly, true, User.ContextLocationAll),
            outletAssignments: [Outlet(AssignedOutlet)]);

        var result = await access.GetAuthorizedLocationsAsync(
            new UserOutletAccessContext(
                UserId,
                false,
                false,
                BusinessId,
                DefaultOutlet,
                EffectiveWarehouseId: 302,
                IsContextSwitched: true),
            LocationAccessPolicy.GrnView);

        Assert.Equal([AssignedOutlet], result.Outlets.Select(location => location.Id));
        Assert.Empty(result.Warehouses);
    }

    [Fact]
    public async Task ForeignBusinessAssignments_AreIgnored()
    {
        var access = CreateAccess(
            new UserLocationAccessUser(BusinessId, null, User.InventoryAccessAssignedOnly),
            outletAssignments: [Outlet(209, businessId: 2)],
            warehouseAssignments: [Warehouse(309, businessId: 2)]);

        var result = await access.GetAuthorizedLocationsAsync(
            new UserOutletAccessContext(UserId, false, false, BusinessId, null),
            LocationAccessPolicy.GrnView);

        Assert.Empty(result.Outlets);
        Assert.Empty(result.Warehouses);
    }

    [Fact]
    public void PolicyName_IsAcceptedFromTheLocationsEndpoint()
    {
        Assert.True(LocationAccessPolicies.TryParse("GrnView", out var policy));
        Assert.Equal(LocationAccessPolicy.GrnView, policy);
    }

    private static UserOutletAssignmentReadModel Outlet(long id, long businessId = BusinessId) =>
        new(id, id, false, $"Outlet {id}", businessId);

    private static UserWarehouseAssignmentReadModel Warehouse(long id, long businessId = BusinessId) =>
        new(id, id, false, $"Warehouse {id}", businessId);

    private static UserOutletAccess CreateAccess(
        UserLocationAccessUser user,
        IReadOnlyList<UserOutletAssignmentReadModel>? outletAssignments = null,
        IReadOnlyList<UserWarehouseAssignmentReadModel>? warehouseAssignments = null)
    {
        var store = new Mock<IUserOutletAccessStore>();
        store.Setup(port => port.FindUserAsync(UserId)).ReturnsAsync(user);
        store.Setup(port => port.ListOutletsAsync(BusinessId)).ReturnsAsync(BusinessOutlets);
        store.Setup(port => port.ListWarehousesAsync(BusinessId)).ReturnsAsync(BusinessWarehouses);
        store.Setup(port => port.ListActiveOutletAssignmentsAsync(UserId)).ReturnsAsync(outletAssignments ?? []);
        store.Setup(port => port.ListActiveWarehouseAssignmentsAsync(UserId)).ReturnsAsync(warehouseAssignments ?? []);
        return new UserOutletAccess(store.Object);
    }
}
