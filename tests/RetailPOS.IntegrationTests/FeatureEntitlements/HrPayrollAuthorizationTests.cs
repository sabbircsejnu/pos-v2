using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.Authorization;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.IntegrationTests.FeatureEntitlements;

public sealed class HrPayrollAuthorizationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Wildcard_DoesNotBypassCapabilityOrBusinessScope(bool allowed)
    {
        var capability = new Mock<IHrPayrollCapabilityService>();
        var setup = capability.Setup(service =>
            service.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, CancellationToken.None));
        if (allowed) setup.Returns(Task.CompletedTask);
        else setup.ThrowsAsync(new UnauthorizedAccessException());
        var requirement = new HrPayrollCapabilityRequirement(HrPayrollCapabilityKeys.Payroll);
        var context = new AuthorizationHandlerContext([requirement],
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "*")], "test")), null);
        await new HrPayrollCapabilityAuthorizationHandler(
            capability.Object, NullLogger<HrPayrollCapabilityAuthorizationHandler>.Instance).HandleAsync(context);

        Assert.Equal(allowed, context.HasSucceeded);
        Assert.Equal(!allowed, context.HasFailed);
    }
}
