using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Outlet;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

/// <summary>
/// Read-only endpoints describing the *current* user's outlet/warehouse access.
/// Frontend uses these to populate scoped filter dropdowns and to determine the
/// default outlet selection for reports.
/// </summary>
[ApiController]
[Route("api/me/outlets")]
[Authorize]
public class UserOutletsController : ControllerBase
{
    private readonly IUserOutletAccessService _access;

    public UserOutletsController(IUserOutletAccessService access)
    {
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<AuthorizedOutletsDto>>> GetAuthorizedOutlets()
    {
        try
        {
            var dto = await _access.GetAuthorizedOutletsAsync();
            return Ok(ApiResponse<AuthorizedOutletsDto>.SuccessResponse(dto));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<AuthorizedOutletsDto>.ErrorResponse(ex.Message));
        }
    }
}
