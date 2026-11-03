using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.OtherIncome.Abstractions;
using RetailPOS.Application.OtherIncome.Commands;
using RetailPOS.Application.OtherIncome.Queries;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/incomes")]
public class AccountingOtherIncomeController : ControllerBase
{
    private readonly ITenantAccessService _tenantAccess;
    private readonly ISender _sender;

    public AccountingOtherIncomeController(
        ITenantAccessService tenantAccess,
        ISender sender)
    {
        _tenantAccess = tenantAccess;
        _sender = sender;
    }

    [HttpGet]
    [Authorize(Policy = "accounting.other_income.view")]
    public async Task<ActionResult<ApiResponse<OtherIncomeListDto>>> Search(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? status,
        [FromQuery] long? outletId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new SearchOtherIncomeQuery(new OtherIncomeSearchFilter(
                _tenantAccess.RequireBusinessId(),
                startDate,
                endDate,
                status,
                outletId,
                pageNumber,
                pageSize)),
            cancellationToken);

        return Ok(ApiResponse<OtherIncomeListDto>.SuccessResponse(MapToDto(result)));
    }

    [HttpPost("search")]
    [Authorize(Policy = "accounting.other_income.view")]
    public async Task<ActionResult<ApiResponse<OtherIncomeListDto>>> SearchPost(
        [FromBody] OtherIncomeSearchDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SearchOtherIncomeQuery(new OtherIncomeSearchFilter(
                _tenantAccess.RequireBusinessId(),
                dto.StartDate,
                dto.EndDate,
                dto.Status,
                dto.OutletId,
                dto.PageNumber,
                dto.PageSize)),
            cancellationToken);
        return Ok(ApiResponse<OtherIncomeListDto>.SuccessResponse(MapToDto(result)));
    }

    [HttpGet("{id:long}")]
    [Authorize(Policy = "accounting.other_income.view")]
    public async Task<ActionResult<ApiResponse<OtherIncomeEntryDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await GetEntryAsync(id, cancellationToken);
        return Ok(ApiResponse<OtherIncomeEntryDto>.SuccessResponse(result));
    }

    [HttpPost]
    [Authorize(Policy = "accounting.other_income.create")]
    public async Task<ActionResult<ApiResponse<OtherIncomeEntryDto>>> Create(
        [FromBody] CreateOtherIncomeEntryDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<OtherIncomeEntryDto>.ErrorResponse("User identity not found"));
        }

        var id = await _sender.Send(
            new CreateOtherIncomeCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                new OtherIncomeCreationInput(
                    dto.IncomeDate,
                    dto.IncomeCategory,
                    dto.IncomeAccountId,
                    dto.Amount,
                    dto.VatRate,
                    dto.VatAmount,
                    dto.TaxCode,
                    dto.TaxInclusive,
                    dto.PaymentMethod,
                    dto.PaymentAccountId,
                    dto.ReferenceNumber,
                    dto.Description,
                    dto.OutletId)),
            cancellationToken);
        var result = await GetEntryAsync(id, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<OtherIncomeEntryDto>.SuccessResponse(result, "Other income created successfully"));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "accounting.other_income.edit")]
    public async Task<ActionResult<ApiResponse<OtherIncomeEntryDto>>> Update(
        long id,
        [FromBody] UpdateOtherIncomeEntryDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<OtherIncomeEntryDto>.ErrorResponse("User identity not found"));
        }

        var entryId = await _sender.Send(
            new UpdateOtherIncomeCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id,
                new OtherIncomeCreationInput(
                    dto.IncomeDate,
                    dto.IncomeCategory,
                    dto.IncomeAccountId,
                    dto.Amount,
                    dto.VatRate,
                    dto.VatAmount,
                    dto.TaxCode,
                    dto.TaxInclusive,
                    dto.PaymentMethod,
                    dto.PaymentAccountId,
                    dto.ReferenceNumber,
                    dto.Description,
                    dto.OutletId)),
            cancellationToken);
        var result = await GetEntryAsync(entryId, cancellationToken);
        return Ok(ApiResponse<OtherIncomeEntryDto>.SuccessResponse(result, "Other income updated successfully"));
    }

    [HttpPost("{id:long}/submit")]
    [Authorize(Policy = "accounting.other_income.submit")]
    public async Task<ActionResult<ApiResponse<OtherIncomeEntryDto>>> Submit(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<OtherIncomeEntryDto>.ErrorResponse("User identity not found"));
        }

        var entryId = await _sender.Send(
            new SubmitOtherIncomeCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id),
            cancellationToken);
        var result = await GetEntryAsync(entryId, cancellationToken);
        return Ok(ApiResponse<OtherIncomeEntryDto>.SuccessResponse(result, "Other income submitted successfully"));
    }

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = "accounting.other_income.approve")]
    public async Task<ActionResult<ApiResponse<OtherIncomeEntryDto>>> Approve(
        long id,
        [FromBody] ApproveOtherIncomeEntryDto? dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<OtherIncomeEntryDto>.ErrorResponse("User identity not found"));
        }

        var entryId = await _sender.Send(
            new ApproveOtherIncomeCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id,
                dto?.IncomeAccountId),
            cancellationToken);
        var result = await GetEntryAsync(entryId, cancellationToken);
        return Ok(ApiResponse<OtherIncomeEntryDto>.SuccessResponse(result, "Other income approved successfully"));
    }

    [HttpPost("{id:long}/reject")]
    [Authorize(Policy = "accounting.other_income.reject")]
    public async Task<ActionResult<ApiResponse<OtherIncomeEntryDto>>> Reject(
        long id,
        [FromBody] RequiredReasonActionDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<OtherIncomeEntryDto>.ErrorResponse("User identity not found"));
        }

        var entryId = await _sender.Send(
            new RejectOtherIncomeCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id,
                dto.Reason),
            cancellationToken);
        var result = await GetEntryAsync(entryId, cancellationToken);
        return Ok(ApiResponse<OtherIncomeEntryDto>.SuccessResponse(result, "Other income rejected successfully"));
    }

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = "accounting.other_income.cancel")]
    public async Task<ActionResult<ApiResponse<OtherIncomeEntryDto>>> Cancel(
        long id,
        [FromBody] RequiredReasonActionDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<OtherIncomeEntryDto>.ErrorResponse("User identity not found"));
        }

        var entryId = await _sender.Send(
            new CancelOtherIncomeCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id,
                dto.Reason),
            cancellationToken);
        var result = await GetEntryAsync(entryId, cancellationToken);
        return Ok(ApiResponse<OtherIncomeEntryDto>.SuccessResponse(result, "Other income cancelled successfully"));
    }

    private bool TryGetCurrentUserId(out long userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(value, out userId);
    }

    private async Task<OtherIncomeEntryDto> GetEntryAsync(long id, CancellationToken cancellationToken)
    {
        var readModel = await _sender.Send(
            new GetOtherIncomeByIdQuery(_tenantAccess.RequireBusinessId(), id),
            cancellationToken);
        return MapToDto(readModel);
    }

    private static OtherIncomeListDto MapToDto(OtherIncomeListReadModel readModel) => new()
    {
        Items = readModel.Items.Select(MapToDto).ToList(),
        TotalCount = readModel.TotalCount,
        PageNumber = readModel.PageNumber,
        PageSize = readModel.PageSize
    };

    private static OtherIncomeEntryDto MapToDto(OtherIncomeEntryReadModel readModel) => new()
    {
        Id = readModel.Id,
        BusinessId = readModel.BusinessId,
        IncomeDate = readModel.IncomeDate,
        IncomeCategory = readModel.IncomeCategory,
        IncomeAccountId = readModel.IncomeAccountId,
        Amount = readModel.Amount,
        PaymentMethod = readModel.PaymentMethod,
        PaymentAccountId = readModel.PaymentAccountId,
        ReferenceNumber = readModel.ReferenceNumber,
        Description = readModel.Description,
        Status = readModel.Status,
        OutletId = readModel.OutletId,
        JournalEntryId = readModel.JournalEntryId,
        CreatedAt = readModel.CreatedAt,
        SubmittedAt = readModel.SubmittedAt,
        ApprovedAt = readModel.ApprovedAt,
        RejectedAt = readModel.RejectedAt,
        RejectionReason = readModel.RejectionReason,
        CancelledAt = readModel.CancelledAt,
        CancellationReason = readModel.CancellationReason
    };
}