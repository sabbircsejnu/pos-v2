using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using RetailPOS.API.Services.HR;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.IntegrationTests.HR;

public sealed class ApprovalKernelAccessAdapterTests
{
    [Theory]
    [InlineData(ApprovalProcessType.Leave)]
    [InlineData(ApprovalProcessType.Overtime)]
    [InlineData(ApprovalProcessType.Exit)]
    public async Task HrApprovalProcessesAllowAuthorizedLocationScope(ApprovalProcessType processType)
    {
        var state = CreateState("hr.approval_policy.manage");

        await state.Access.EnsureAsync(
            7, processType, ApprovalKernelOperation.ManagePolicy,
            ApprovalScopeType.Location, "42", CancellationToken.None);

        state.Organisation.Verify(service => service.EnsureLocationAccessibleAsync(
            7, 42, It.IsAny<CancellationToken>()), Times.Once);
        state.Capabilities.Verify(service => service.EnsureEnabledAsync(
            HrPayrollCapabilityKeys.Hr, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveLocationScopeRejectsInvalidOrUnauthorizedLocation()
    {
        var invalid = CreateState("hr.leave.request");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => invalid.Access.EnsureAsync(
            7, ApprovalProcessType.Leave, ApprovalKernelOperation.Submit,
            ApprovalScopeType.Location, "not-a-location", CancellationToken.None));

        var unauthorized = CreateState("hr.leave.request");
        unauthorized.Organisation.Setup(service => service.EnsureLocationAccessibleAsync(
                7, 42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Location scope denied."));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unauthorized.Access.EnsureAsync(
            7, ApprovalProcessType.Leave, ApprovalKernelOperation.Submit,
            ApprovalScopeType.Location, "42", CancellationToken.None));
    }

    [Fact]
    public async Task PayrollApprovalDoesNotInheritHrLocationScope()
    {
        var state = CreateState("payroll.approval_policy.manage");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => state.Access.EnsureAsync(
            7, ApprovalProcessType.PayrollRun, ApprovalKernelOperation.ManagePolicy,
            ApprovalScopeType.Location, "42", CancellationToken.None));

        state.Organisation.Verify(service => service.EnsureLocationAccessibleAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AccessState CreateState(string permission)
    {
        var audit = new AuditContext();
        audit.Initialize(AuditContext.ForBusinessSystemJob("approval-access-tests", 7));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("permission", permission)], "test"));
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        var capabilities = new Mock<IHrPayrollCapabilityService>();
        capabilities.Setup(service => service.EnsureEnabledAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var organisation = new Mock<IHrOrganisationAccess>();
        var organisationReadStore = new Mock<IHrOrganisationReadStore>();
        var access = new ApprovalKernelAccessAdapter(
            accessor, audit, capabilities.Object, organisation.Object, organisationReadStore.Object);
        return new AccessState(access, capabilities, organisation);
    }

    private sealed record AccessState(
        ApprovalKernelAccessAdapter Access,
        Mock<IHrPayrollCapabilityService> Capabilities,
        Mock<IHrOrganisationAccess> Organisation);
}
