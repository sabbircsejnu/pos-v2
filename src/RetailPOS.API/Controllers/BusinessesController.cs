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

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<BusinessSummaryDto>>>> GetBusinesses(
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null)
    {
        var businesses = await _onboardingService.GetBusinessesAsync(search, isActive);
        return Ok(ApiResponse<List<BusinessSummaryDto>>.SuccessResponse(businesses));
    }

    [HttpGet("{businessId:long}")]
    public async Task<ActionResult<ApiResponse<BusinessSummaryDto>>> GetBusinessById([FromRoute] long businessId)
    {
        var business = await _onboardingService.GetBusinessByIdAsync(businessId);
        return Ok(ApiResponse<BusinessSummaryDto>.SuccessResponse(business));
    }

    [HttpPatch("{businessId:long}/status")]
    public async Task<ActionResult<ApiResponse<BusinessSummaryDto>>> UpdateStatus(
        [FromRoute] long businessId,
        [FromBody] UpdateBusinessStatusDto request)
    {
        var updated = await _onboardingService.SetBusinessActiveAsync(businessId, request.IsActive);
        var message = request.IsActive ? "Business activated" : "Business deactivated";
        return Ok(ApiResponse<BusinessSummaryDto>.SuccessResponse(updated, message));
    }

    [HttpPatch("{businessId:long}/subscription")]
    public async Task<ActionResult<ApiResponse<BusinessSummaryDto>>> UpdateSubscription(
        [FromRoute] long businessId,
        [FromBody] UpdateBusinessSubscriptionDto request)
    {
        var updated = await _onboardingService.UpdateSubscriptionAsync(businessId, request);
        return Ok(ApiResponse<BusinessSummaryDto>.SuccessResponse(updated, "Business subscription updated"));
    }

    [HttpGet("{businessId:long}/features")]
    public async Task<ActionResult<ApiResponse<List<BusinessFeatureSettingDto>>>> GetFeatureSettings([FromRoute] long businessId)
    {
        var result = await _onboardingService.GetFeatureSettingsAsync(businessId);
        return Ok(ApiResponse<List<BusinessFeatureSettingDto>>.SuccessResponse(result));
    }

    [HttpPatch("{businessId:long}/features")]
    public async Task<ActionResult<ApiResponse<List<BusinessFeatureSettingDto>>>> UpsertFeatureSettings(
        [FromRoute] long businessId,
        [FromBody] UpdateBusinessFeatureSettingsDto request)
    {
        var result = await _onboardingService.UpsertFeatureSettingsAsync(businessId, request);
        return Ok(ApiResponse<List<BusinessFeatureSettingDto>>.SuccessResponse(result, "Business feature settings updated"));
    }

    [HttpPost("{businessId:long}/owner/reset-access")]
    public async Task<ActionResult<ApiResponse<ResetBusinessOwnerAccessResponseDto>>> ResetOwnerAccess([FromRoute] long businessId)
    {
        var response = await _onboardingService.ResetBusinessOwnerAccessAsync(businessId);
        return Ok(ApiResponse<ResetBusinessOwnerAccessResponseDto>.SuccessResponse(
            response,
            "Business owner access reset invitation issued"));
    }
}
