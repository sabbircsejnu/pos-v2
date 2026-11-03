using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.API.Services;
using RetailPOS.API.Services.HR;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrOrganisationAccessAdapterTests
{
    [Fact]
    public async Task RequiresMatchingBusinessAndEnabledCapability()
    {
        var state = CreateState(["hr.organisation.view"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => state.Access.EnsureAsync(
            8, HrOrganisationOperation.ViewOrgUnits, null, default));

        state.Capabilities.Setup(service => service.EnsureEnabledAsync(
                HrPayrollCapabilityKeys.Hr, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("HR is disabled."));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => state.Access.EnsureAsync(
            7, HrOrganisationOperation.ViewOrgUnits, null, default));
    }

    [Fact]
    public async Task RequiresPermissionAndEnforcesLocationProjectionScope()
    {
        var denied = CreateState([]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.Access.EnsureAsync(
            7, HrOrganisationOperation.ViewOperationalLocations, null, default));

        var allowed = CreateState(["hr.location.view"]);
        allowed.ReadStore.Setup(store => store.IsLocationAccessibleAsync(
                7, 99, It.IsAny<HrOperationalLocationReadScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => allowed.Access.EnsureAsync(
            7, HrOrganisationOperation.ViewOperationalLocations, 99, default));

        allowed.LocationAccess.Setup(service => service.GetAuthorizedOutletsAsync())
            .ReturnsAsync(new AuthorizedOutletsDto
            {
                IsGlobalAccess = false,
                Outlets = [new AuthorizedLocationDto { Id = 12 }],
                Warehouses = [new AuthorizedLocationDto { Id = 14 }]
            });
        var scope = await allowed.Access.GetLocationReadScopeAsync(7, default);
        Assert.Equal(
            new[]
            {
                new OperationalLocationSourceIdentity("Outlet", "12"),
                new OperationalLocationSourceIdentity("Warehouse", "14")
            },
            scope.AllowedSources);
    }

    [Fact]
    public async Task ReconciliationRequiresBusinessWideLocationAccess()
    {
        var scoped = CreateState(["hr.location.manage"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scoped.Access.EnsureAsync(
            7, HrOrganisationOperation.ReconcileOperationalLocations, null, default));

        var global = CreateState(["hr.location.manage"]);
        global.LocationAccess.Setup(service => service.GetAuthorizedOutletsAsync())
            .ReturnsAsync(new AuthorizedOutletsDto { IsGlobalAccess = true });
        await global.Access.EnsureAsync(
            7, HrOrganisationOperation.ReconcileOperationalLocations, null, default);
    }

    [Fact]
    public async Task PositionOperationsRequirePositionPermissions()
    {
        var denied = CreateState(["hr.organisation.view"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.Access.EnsureAsync(
            7, HrOrganisationOperation.ViewPositions, null, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.Access.EnsureAsync(
            7, HrOrganisationOperation.ManagePositions, null, default));

        var allowed = CreateState(["hr.position.view", "hr.position.manage"]);
        await allowed.Access.EnsureAsync(7, HrOrganisationOperation.ViewPositions, null, default);
        await allowed.Access.EnsureAsync(7, HrOrganisationOperation.ManagePositions, null, default);
    }

    [Fact]
    public async Task EmployeeOperationsRequireTheirOwnPermissionsAndHrCapability()
    {
        var denied = CreateState(["hr.organisation.view"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.Access.EnsureAsync(
            7, HrOrganisationOperation.ViewEmployees, null, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.Access.EnsureAsync(
            7, HrOrganisationOperation.CreateEmployees, null, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.Access.EnsureAsync(
            7, HrOrganisationOperation.EditEmployees, null, default));

        var allowed = CreateState(["hr.employee.view", "hr.employee.create", "hr.employee.edit"]);
        await allowed.Access.EnsureAsync(7, HrOrganisationOperation.ViewEmployees, null, default);
        await allowed.Access.EnsureAsync(7, HrOrganisationOperation.CreateEmployees, null, default);
        await allowed.Access.EnsureAsync(7, HrOrganisationOperation.EditEmployees, null, default);
        allowed.Capabilities.Setup(service => service.EnsureEnabledAsync(
                HrPayrollCapabilityKeys.Hr, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("HR is disabled."));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => allowed.Access.EnsureAsync(
            7, HrOrganisationOperation.ViewEmployees, null, default));
    }

    private static AccessState CreateState(string[] permissions)
    {
        var context = new AuditContext();
        context.Initialize(AuditContext.ForBusinessSystemJob("hr-access-tests", 7));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            permissions.Select(value => new Claim("permission", value)), "test"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var capabilityService = new Mock<IHrPayrollCapabilityService>();
        capabilityService.Setup(service => service.EnsureEnabledAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var tenantAccess = new Mock<ITenantAccessService>();
        tenantAccess.Setup(service => service.RequireBusinessId()).Returns(7);
        var locations = new Mock<IUserOutletAccessService>();
        locations.Setup(service => service.GetAuthorizedOutletsAsync())
            .ReturnsAsync(new AuthorizedOutletsDto());
        var readStore = new Mock<IHrOrganisationReadStore>();
        readStore.Setup(store => store.IsLocationAccessibleAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<HrOperationalLocationReadScope>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var access = new HrOrganisationAccessAdapter(accessor, context, capabilityService.Object,
            tenantAccess.Object, locations.Object, readStore.Object,
            Mock.Of<IHrEmployeeLocationAssignmentStore>(), Mock.Of<IHrEmployeeDocumentStore>(),
            Mock.Of<IHrEmployeeDocumentReadStore>(), Mock.Of<IHrEmployeeStore>(),
            Mock.Of<IHrLeaveRequestStore>());
        return new AccessState(access, capabilityService, locations, readStore);
    }

    private sealed record AccessState(
        HrOrganisationAccessAdapter Access,
        Mock<IHrPayrollCapabilityService> Capabilities,
        Mock<IUserOutletAccessService> LocationAccess,
        Mock<IHrOrganisationReadStore> ReadStore);
}
