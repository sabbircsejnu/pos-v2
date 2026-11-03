using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.Application.FinancialPeriods.Commands;
using RetailPOS.Application.FinancialPeriods.Queries;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting")]
public class AccountingFinancialPeriodsController : ControllerBase
{
    private readonly ISender _sender;

    public AccountingFinancialPeriodsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("financial-years")]
    [Authorize(Policy = "accounting.financial_years.view")]
    public async Task<ActionResult<ApiResponse<List<FinancialYearDto>>>> GetFinancialYears(
        CancellationToken cancellationToken)
    {
        var years = await _sender.Send(new GetFinancialYearsQuery(), cancellationToken);
        var result = years.Select(year => new FinancialYearDto
        {
            Id = year.Id,
            YearCode = year.YearCode,
            Name = year.Name,
            StartDate = year.StartDate,
            EndDate = year.EndDate,
            IsCurrent = year.IsCurrent,
            IsClosed = year.IsClosed,
            ClosedAt = year.ClosedAt,
            ClosedBy = year.ClosedBy
        }).ToList();
        return Ok(ApiResponse<List<FinancialYearDto>>.SuccessResponse(result));
    }

    [HttpPost("financial-years")]
    [Authorize(Policy = "accounting.financial_years.manage")]
    public async Task<ActionResult<ApiResponse<FinancialYearDto>>> CreateFinancialYear(
        [FromBody] CreateFinancialYearDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<FinancialYearDto>.ErrorResponse("User identity not found"));
        }

        var year = await _sender.Send(
            new CreateFinancialYearCommand(
                dto.YearCode,
                dto.Name,
                dto.StartDate,
                dto.EndDate,
                dto.IsCurrent,
                userId),
            cancellationToken);
        return Ok(ApiResponse<FinancialYearDto>.SuccessResponse(MapYear(year), "Financial year created successfully"));
    }

    [HttpPut("financial-years/{id:long}")]
    [Authorize(Policy = "accounting.financial_years.manage")]
    public async Task<ActionResult<ApiResponse<FinancialYearDto>>> UpdateFinancialYear(
        long id,
        [FromBody] UpdateFinancialYearDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<FinancialYearDto>.ErrorResponse("User identity not found"));
        }

        var year = await _sender.Send(
            new UpdateFinancialYearCommand(
                id,
                dto.YearCode,
                dto.Name,
                dto.StartDate,
                dto.EndDate,
                userId),
            cancellationToken);
        return Ok(ApiResponse<FinancialYearDto>.SuccessResponse(MapYear(year), "Financial year updated successfully"));
    }

    [HttpPost("financial-years/{id:long}/activate")]
    [Authorize(Policy = "accounting.financial_years.manage")]
    public async Task<ActionResult<ApiResponse<FinancialYearDto>>> ActivateFinancialYear(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<FinancialYearDto>.ErrorResponse("User identity not found"));
        }

        var year = await _sender.Send(
            new ActivateFinancialYearCommand(id, userId),
            cancellationToken);
        return Ok(ApiResponse<FinancialYearDto>.SuccessResponse(MapYear(year), "Financial year activated successfully"));
    }

    [HttpPost("financial-years/{id:long}/close")]
    [Authorize(Policy = "accounting.financial_years.manage")]
    public async Task<ActionResult<ApiResponse<FinancialYearDto>>> CloseFinancialYear(long id, [FromBody] FinancialYearActionDto? dto = null)
    {
        throw new InvalidOperationException("Use the approved financial year close-run workflow to close a financial year.");
    }

    [HttpPost("financial-years/{id:long}/reopen")]
    [Authorize(Policy = "accounting.financial_years.manage")]
    public async Task<ActionResult<ApiResponse<FinancialYearDto>>> ReopenFinancialYear(long id, [FromBody] RequiredReasonActionDto dto)
    {
        throw new InvalidOperationException("Use the financial year close-run reopen workflow, which requires reversal of the effective YearEndClosing journal.");
    }

    [HttpGet("financial-years/{financialYearId:long}/periods")]
    [Authorize(Policy = "accounting.periods.view")]
    public async Task<ActionResult<ApiResponse<List<AccountingPeriodDto>>>> GetPeriods(
        long financialYearId,
        CancellationToken cancellationToken)
    {
        var periods = await _sender.Send(
            new GetAccountingPeriodsQuery(financialYearId),
            cancellationToken);
        var result = periods.Select(period => new AccountingPeriodDto
        {
            Id = period.Id,
            FinancialYearId = period.FinancialYearId,
            PeriodNumber = period.PeriodNumber,
            Name = period.Name,
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            Status = period.Status,
            StatusReason = period.StatusReason,
            ClosedAt = period.ClosedAt,
            ClosedBy = period.ClosedBy,
            LockedAt = period.LockedAt,
            LockedBy = period.LockedBy,
            ReopenedAt = period.ReopenedAt,
            ReopenedBy = period.ReopenedBy
        }).ToList();
        return Ok(ApiResponse<List<AccountingPeriodDto>>.SuccessResponse(result));
    }

    [HttpPost("financial-years/{financialYearId:long}/periods")]
    [Authorize(Policy = "accounting.financial_years.manage")]
    public async Task<ActionResult<ApiResponse<AccountingPeriodDto>>> CreatePeriod(
        long financialYearId,
        [FromBody] CreateAccountingPeriodDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<AccountingPeriodDto>.ErrorResponse("User identity not found"));
        }

        var period = await _sender.Send(
            new CreateAccountingPeriodCommand(
                financialYearId,
                dto.PeriodNumber,
                dto.Name,
                dto.StartDate,
                dto.EndDate,
                dto.Status,
                userId),
            cancellationToken);
        return Ok(ApiResponse<AccountingPeriodDto>.SuccessResponse(MapPeriod(period), "Accounting period created successfully"));
    }

    [HttpPut("periods/{id:long}")]
    [Authorize(Policy = "accounting.financial_years.manage")]
    public async Task<ActionResult<ApiResponse<AccountingPeriodDto>>> UpdatePeriod(
        long id,
        [FromBody] UpdateAccountingPeriodDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<AccountingPeriodDto>.ErrorResponse("User identity not found"));
        }

        var period = await _sender.Send(
            new UpdateAccountingPeriodCommand(
                id,
                dto.PeriodNumber,
                dto.Name,
                dto.StartDate,
                dto.EndDate,
                userId),
            cancellationToken);
        return Ok(ApiResponse<AccountingPeriodDto>.SuccessResponse(MapPeriod(period), "Accounting period updated successfully"));
    }

    [HttpPost("periods/{id:long}/close")]
    [Authorize(Policy = "accounting.periods.close")]
    public Task<ActionResult<ApiResponse<AccountingPeriodDto>>> ClosePeriod(long id, [FromBody] AccountingPeriodStatusActionDto? dto = null)
    {
        throw new InvalidOperationException("Use the approved accounting period close-run workflow to close a period.");
    }

    [HttpPost("periods/{id:long}/lock")]
    [Authorize(Policy = "accounting.periods.close")]
    public Task<ActionResult<ApiResponse<AccountingPeriodDto>>> LockPeriod(
        long id,
        [FromBody] RequiredReasonActionDto dto,
        CancellationToken cancellationToken)
    {
        return UpdatePeriodStatus(
            id,
            AccountingPeriodStatuses.Locked,
            dto.Reason,
            cancellationToken);
    }

    [HttpPost("periods/{id:long}/reopen")]
    [Authorize(Policy = "accounting.periods.reopen")]
    public Task<ActionResult<ApiResponse<AccountingPeriodDto>>> ReopenPeriod(
        long id,
        [FromBody] RequiredReasonActionDto dto,
        CancellationToken cancellationToken)
    {
        return UpdatePeriodStatus(
            id,
            AccountingPeriodStatuses.Open,
            dto.Reason,
            cancellationToken);
    }

    private async Task<ActionResult<ApiResponse<AccountingPeriodDto>>> UpdatePeriodStatus(
        long id,
        string status,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(ApiResponse<AccountingPeriodDto>.ErrorResponse("User identity not found"));
        }

        var period = await _sender.Send(
            new UpdateAccountingPeriodStatusCommand(id, status, reason, userId),
            cancellationToken);
        return Ok(ApiResponse<AccountingPeriodDto>.SuccessResponse(MapPeriod(period), "Accounting period status updated successfully"));
    }

    private bool TryGetCurrentUserId(out long userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(value, out userId);
    }

    private static FinancialYearDto MapYear(
        RetailPOS.Application.FinancialPeriods.ReadModels.FinancialYearReadModel year) =>
        new()
        {
            Id = year.Id,
            YearCode = year.YearCode,
            Name = year.Name,
            StartDate = year.StartDate,
            EndDate = year.EndDate,
            IsCurrent = year.IsCurrent,
            IsClosed = year.IsClosed,
            ClosedAt = year.ClosedAt,
            ClosedBy = year.ClosedBy
        };

    private static AccountingPeriodDto MapPeriod(
        RetailPOS.Application.FinancialPeriods.ReadModels.AccountingPeriodReadModel period) =>
        new()
        {
            Id = period.Id,
            FinancialYearId = period.FinancialYearId,
            PeriodNumber = period.PeriodNumber,
            Name = period.Name,
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            Status = period.Status,
            StatusReason = period.StatusReason,
            ClosedAt = period.ClosedAt,
            ClosedBy = period.ClosedBy,
            LockedAt = period.LockedAt,
            LockedBy = period.LockedBy,
            ReopenedAt = period.ReopenedAt,
            ReopenedBy = period.ReopenedBy
        };
}
