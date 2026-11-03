using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.BusinessAdministration.Commands;
using RetailPOS.Application.BusinessAdministration.Queries;
using RetailPOS.API.DTOs.Business;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/businesses")]
[Authorize(Roles = RoleSwitchClaims.SuperAdminRoleName)]
public class BusinessesController : ControllerBase
{
    private readonly ISender _sender;

    public BusinessesController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CreateBusinessResponseDto>>> CreateBusiness(
        [FromBody] CreateBusinessRequestDto request,
        CancellationToken cancellationToken)
    {
        var created = await _sender.Send(
            new CreateBusinessCommand(
                request.BusinessName,
                request.BusinessEmail,
                request.BusinessPhone,
                request.BusinessAddress,
                request.OwnerName,
                request.OwnerEmail,
                request.OutletManagerName,
                request.OutletManagerEmail,
                request.SalesPersonName,
                request.SalesPersonEmail,
                request.AccountsAdminName,
                request.AccountsAdminEmail,
                request.DefaultOutletName,
                request.CreateDefaultWarehouse,
                request.DefaultWarehouseName,
                request.WarehouseManagerName,
                request.WarehouseManagerEmail),
            cancellationToken);
        var response = new CreateBusinessResponseDto
        {
            BusinessId = created.BusinessId,
            BusinessName = created.BusinessName,
            DefaultOutletId = created.DefaultOutletId,
            DefaultWarehouseId = created.DefaultWarehouseId,
            Users = created.Users.Select(user => new OnboardedUserDto
            {
                UserId = user.UserId,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role,
                InvitationToken = user.InvitationToken,
                InvitationExpiresAt = user.InvitationExpiresAt
            }).ToList()
        };
        return Ok(ApiResponse<CreateBusinessResponseDto>.SuccessResponse(response, "Business onboarded successfully"));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<BusinessSummaryDto>>>> GetBusinesses(
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var businesses = await _sender.Send(new GetBusinessesQuery(search, isActive), cancellationToken);
        return Ok(ApiResponse<List<BusinessSummaryDto>>.SuccessResponse(
            businesses.Select(MapBusinessSummary).ToList()));
    }

    [HttpGet("{businessId:long}")]
    public async Task<ActionResult<ApiResponse<BusinessSummaryDto>>> GetBusinessById(
        [FromRoute] long businessId,
        CancellationToken cancellationToken)
    {
        var business = await _sender.Send(new GetBusinessByIdQuery(businessId), cancellationToken);
        return Ok(ApiResponse<BusinessSummaryDto>.SuccessResponse(MapBusinessSummary(business)));
    }

    [HttpPatch("{businessId:long}/status")]
    public async Task<ActionResult<ApiResponse<BusinessSummaryDto>>> UpdateStatus(
        [FromRoute] long businessId,
        [FromBody] UpdateBusinessStatusDto request,
        CancellationToken cancellationToken)
    {
        var updated = await _sender.Send(
            new SetBusinessActiveCommand(businessId, request.IsActive),
            cancellationToken);
        var message = request.IsActive ? "Business activated" : "Business deactivated";
        return Ok(ApiResponse<BusinessSummaryDto>.SuccessResponse(MapBusinessSummary(updated), message));
    }

    [HttpPatch("{businessId:long}/subscription")]
    public async Task<ActionResult<ApiResponse<BusinessSummaryDto>>> UpdateSubscription(
        [FromRoute] long businessId,
        [FromBody] UpdateBusinessSubscriptionDto request,
        CancellationToken cancellationToken)
    {
        var updated = await _sender.Send(
            new UpdateBusinessSubscriptionCommand(
                businessId,
                request.SubscriptionPlan,
                request.TrialEndsAt,
                request.SubscriptionEndsAt,
                request.MaxOutlets,
                request.MaxUsers),
            cancellationToken);
        return Ok(ApiResponse<BusinessSummaryDto>.SuccessResponse(
            MapBusinessSummary(updated),
            "Business subscription updated"));
    }

    [HttpGet("{businessId:long}/features")]
    public async Task<ActionResult<ApiResponse<List<BusinessFeatureSettingDto>>>> GetFeatureSettings(
        [FromRoute] long businessId,
        CancellationToken cancellationToken)
    {
        var settings = await _sender.Send(
            new GetBusinessFeatureSettingsQuery(businessId),
            cancellationToken);
        var result = settings.Select(setting => new BusinessFeatureSettingDto
        {
            FeatureKey = setting.FeatureKey,
            IsEnabled = setting.IsEnabled,
            LimitValue = setting.LimitValue
        }).ToList();
        return Ok(ApiResponse<List<BusinessFeatureSettingDto>>.SuccessResponse(result));
    }

    [HttpPatch("{businessId:long}/features")]
    public async Task<ActionResult<ApiResponse<List<BusinessFeatureSettingDto>>>> UpsertFeatureSettings(
        [FromRoute] long businessId,
        [FromBody] UpdateBusinessFeatureSettingsDto request,
        CancellationToken cancellationToken)
    {
        var changes = request.Features
            .Select(feature => new RetailPOS.Application.BusinessAdministration.Abstractions.BusinessFeatureSettingChange(
                feature.FeatureKey,
                feature.IsEnabled,
                feature.LimitValue))
            .ToList();
        var settings = await _sender.Send(
            new UpsertBusinessFeatureSettingsCommand(businessId, changes),
            cancellationToken);
        var result = settings.Select(setting => new BusinessFeatureSettingDto
        {
            FeatureKey = setting.FeatureKey,
            IsEnabled = setting.IsEnabled,
            LimitValue = setting.LimitValue
        }).ToList();
        return Ok(ApiResponse<List<BusinessFeatureSettingDto>>.SuccessResponse(result, "Business feature settings updated"));
    }

    [HttpPost("{businessId:long}/owner/reset-access")]
    public async Task<ActionResult<ApiResponse<ResetBusinessOwnerAccessResponseDto>>> ResetOwnerAccess(
        [FromRoute] long businessId,
        CancellationToken cancellationToken)
    {
        var reset = await _sender.Send(
            new ResetBusinessOwnerAccessCommand(businessId),
            cancellationToken);
        var response = new ResetBusinessOwnerAccessResponseDto
        {
            UserId = reset.UserId,
            OwnerEmail = reset.OwnerEmail,
            InvitationToken = reset.InvitationToken,
            InvitationExpiresAt = reset.InvitationExpiresAt
        };
        return Ok(ApiResponse<ResetBusinessOwnerAccessResponseDto>.SuccessResponse(
            response,
            "Business owner access reset invitation issued"));
    }

    private static BusinessSummaryDto MapBusinessSummary(
        RetailPOS.Application.BusinessAdministration.ReadModels.BusinessSummaryReadModel business) =>
        new()
        {
            BusinessId = business.BusinessId,
            BusinessName = business.BusinessName,
            BusinessEmail = business.BusinessEmail,
            BusinessPhone = business.BusinessPhone,
            IsActive = business.IsActive,
            SubscriptionPlan = business.SubscriptionPlan,
            TrialEndsAt = business.TrialEndsAt,
            SubscriptionEndsAt = business.SubscriptionEndsAt,
            MaxOutlets = business.MaxOutlets,
            MaxUsers = business.MaxUsers,
            UserCount = business.UserCount,
            OutletCount = business.OutletCount,
            WarehouseCount = business.WarehouseCount,
            BusinessOwnerEmail = business.BusinessOwnerEmail
        };
}
