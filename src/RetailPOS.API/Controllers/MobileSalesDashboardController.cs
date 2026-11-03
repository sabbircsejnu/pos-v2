using RetailPOS.Application.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Mobile;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.SalesReports;
using MediatR;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/mobile/v1/dashboard/sales")]
[Authorize]
public sealed class MobileSalesDashboardController(
    ISender sender,
    IRoleSwitchContext roleSwitch,
    IMobileSingleBusinessReadinessGuard deploymentGuard,
    ILogger<MobileSalesDashboardController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<MobileSalesDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MobileSalesDashboardDto>>> Get(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        if (!TryResolveSalesReadScope(out var cashierId, out var businessId))
            return Forbid();

        var endUtc = endDate?.ToUniversalTime() ?? DateTime.UtcNow;
        var startUtc = startDate?.ToUniversalTime() ?? endUtc.AddDays(-30);
        if (startUtc > endUtc)
        {
            return BadRequest(ApiResponse<MobileSalesDashboardDto>.ErrorResponse(
                "startDate must be earlier than or equal to endDate."));
        }

        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
        {
            return Forbid();
        }

        var result = await sender.Send(
            new GetMobileSalesDashboardQuery(startUtc, endUtc, cashierId),
            cancellationToken);

        return Ok(ApiResponse<MobileSalesDashboardDto>.SuccessResponse(new MobileSalesDashboardDto(
            result.StartDateUtc,
            result.EndDateUtc,
            result.TotalRevenue,
            result.NetRevenue,
            result.InvoiceCount,
            result.UnitCount,
            result.AverageInvoiceValue)));
    }

    /// <summary>Returns UTC daily counts of eligible invoices without financial measures.</summary>
    [HttpGet("~/api/mobile/v1/insights/sales/activity")]
    [ProducesResponseType(typeof(ApiResponse<MobileSalesActivityTrendDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<MobileSalesActivityTrendDto>>> GetSalesActivityTrend(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        if (!TryResolveSalesReadScope(out var cashierId, out var businessId))
            return Forbid();

        var endUtc = endDate?.ToUniversalTime() ?? DateTime.UtcNow;
        var startUtc = startDate?.ToUniversalTime() ?? endUtc.AddDays(-30);
        if (startUtc > endUtc)
        {
            return BadRequest(new ApiResponse<MobileSalesActivityTrendDto>
            {
                Success = false,
                Message = "INVALID_DATE_RANGE",
                Errors = ["startDate must be earlier than or equal to endDate."]
            });
        }

        if ((endUtc.Date - startUtc.Date).TotalDays > 365)
        {
            return BadRequest(new ApiResponse<MobileSalesActivityTrendDto>
            {
                Success = false,
                Message = "DATE_RANGE_TOO_LARGE",
                Errors = ["The requested date range must not exceed 366 UTC calendar days."]
            });
        }

        if (!await deploymentGuard.IsReadyAsync(businessId, cancellationToken))
            return Forbid();

        var trend = await sender.Send(
            new GetMobileDailySalesActivityQuery(startUtc, endUtc, cashierId),
            cancellationToken);
        var invoiceCounts = trend.ToDictionary(
            item => DateOnly.FromDateTime(item.DateUtc),
            item => item.InvoiceCount);
        var days = new List<MobileDailySalesActivityDto>();
        for (var date = DateOnly.FromDateTime(startUtc); date <= DateOnly.FromDateTime(endUtc); date = date.AddDays(1))
        {
            days.Add(new MobileDailySalesActivityDto(date, invoiceCounts.GetValueOrDefault(date)));
        }

        return Ok(ApiResponse<MobileSalesActivityTrendDto>.SuccessResponse(
            new MobileSalesActivityTrendDto(
                startUtc,
                endUtc,
                "UTC",
                days)));
    }

    private bool TryResolveSalesReadScope(out long? cashierId, out long businessId)
    {
        cashierId = null;
        businessId = 0;
        if (roleSwitch.IsRoleSwitched)
        {
            logger.LogWarning("Mobile sales read denied for a role-switched session.");
            return false;
        }

        var role = roleSwitch.EffectiveRoleName;
        if (IsSalesperson(role))
        {
            if (!User.HasPermission("sales.view") || !roleSwitch.RealUserId.HasValue)
                return false;

            cashierId = roleSwitch.RealUserId.Value;
        }
        else if (IsBusinessOwnerOrManager(role))
        {
            if (!User.HasPermission("reports.sales"))
                return false;
        }
        else
        {
            logger.LogWarning("Mobile sales read denied for an unsupported effective role.");
            return false;
        }

        if (roleSwitch.IsSuperAdmin || !roleSwitch.EffectiveBusinessId.HasValue)
        {
            logger.LogWarning("Mobile sales read denied because the authenticated Business context is absent or globally scoped.");
            return false;
        }

        businessId = roleSwitch.EffectiveBusinessId.Value;
        return true;
    }

    private static bool IsBusinessOwnerOrManager(string? role) =>
        string.Equals(role, "BusinessOwner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "SalesManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "OutletManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);

    private static bool IsSalesperson(string? role) =>
        string.Equals(role, "SalesPerson", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Salesman", StringComparison.OrdinalIgnoreCase);
}
