using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Business;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/businesses")]
[Authorize(Roles = RoleSwitchClaims.SuperAdminRoleName)]
public class BusinessesController : ControllerBase
{
    private readonly IBusinessOnboardingService _onboardingService;

    public BusinessesController(IBusinessOnboardingService onboardingService)
    {
        _onboardingService = onboardingService;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CreateBusinessResponseDto>>> CreateBusiness([FromBody] CreateBusinessRequestDto request)
    {
        var created = await _onboardingService.CreateBusinessAsync(request);
        return Ok(ApiResponse<CreateBusinessResponseDto>.SuccessResponse(created, "Business onboarded successfully"));
    }
}
