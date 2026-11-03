using Microsoft.AspNetCore.Authorization;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.API.Authorization;

public sealed record HrPayrollCapabilityRequirement(string Capability) : IAuthorizationRequirement;

public sealed class HrPayrollCapabilityAuthorizationHandler(
    IHrPayrollCapabilityService capabilities,
    ILogger<HrPayrollCapabilityAuthorizationHandler> logger)
    : AuthorizationHandler<HrPayrollCapabilityRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, HrPayrollCapabilityRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Fail();
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        try
        {
            await capabilities.EnsureEnabledAsync(requirement.Capability, cancellationToken);
            context.Succeed(requirement);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or BusinessContextRequiredException)
        {
            // Fail closed: a missing Business scope or a disabled capability both deny the policy.
            logger.LogWarning("HR/Payroll authorization denied for capability {Capability}.", requirement.Capability);
            context.Fail();
        }
    }
}
