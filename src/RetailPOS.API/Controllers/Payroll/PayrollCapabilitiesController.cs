using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.API.Controllers.Payroll;

/// <summary>Returns the enabled Payroll capability for the validated Business.</summary>
[ApiController]
[Authorize(Policy = "payroll.dashboard.view")]
[Route("api/payroll/v1/capabilities")]
public sealed class PayrollCapabilitiesController(IHrPayrollCapabilityService capabilities) : ControllerBase
{
    /// <summary>Returns Payroll availability after capability and permission authorization.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PayrollCapabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PayrollCapabilityResponse>> Get(CancellationToken cancellationToken)
    {
        var result = await capabilities.GetAsync(cancellationToken);
        if (!result.PayrollEnabled)
            return Forbid();

        return Ok(new PayrollCapabilityResponse(result.BusinessId, result.PayrollEnabled));
    }
}

/// <summary>Payroll capability status for the current Business.</summary>
public sealed record PayrollCapabilityResponse(long BusinessId, bool PayrollEnabled);
