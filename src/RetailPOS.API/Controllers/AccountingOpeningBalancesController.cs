using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.OpeningBalances.Abstractions;
using RetailPOS.Application.OpeningBalances.Commands;
using RetailPOS.Application.OpeningBalances.Queries;
using RetailPOS.Application.AccountingJournals.Queries;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/opening-balances")]
public class AccountingOpeningBalancesController : ControllerBase
{
    private readonly IUserOutletAccessService _outletAccessService;
    private readonly ITenantAccessService _tenantAccess;
    private readonly ISender _sender;

    public AccountingOpeningBalancesController(
        IUserOutletAccessService outletAccessService,
        ITenantAccessService tenantAccess,
        ISender sender)
    {
        _outletAccessService = outletAccessService;
        _tenantAccess = tenantAccess;
        _sender = sender;
    }

    [HttpGet("status")]
    [Authorize(Policy = "accounting.opening_balances.post")]
    public async Task<ActionResult<ApiResponse<OpeningBalanceStatusDto>>> GetStatus(
        CancellationToken cancellationToken)
    {
        var status = await _sender.Send(
            new GetOpeningBalanceStatusQuery(_tenantAccess.RequireBusinessId()),
            cancellationToken);
        var result = new OpeningBalanceStatusDto
        {
            HasPostedOpeningBalance = status.HasPostedOpeningBalance,
            OpeningBalanceJournalEntryId = status.OpeningBalanceJournalEntryId,
            OpeningBalanceJournalNumber = status.OpeningBalanceJournalNumber,
            OpeningBalancePostedAt = status.OpeningBalancePostedAt,
            IsOpeningBalanceLocked = status.IsOpeningBalanceLocked,
            LockingJournalEntryId = status.LockingJournalEntryId,
            LockingJournalNumber = status.LockingJournalNumber,
            LockingJournalType = status.LockingJournalType,
            LockedAt = status.LockedAt,
            CanPostOpeningBalance = status.CanPostOpeningBalance
        };
        return Ok(ApiResponse<OpeningBalanceStatusDto>.SuccessResponse(result));
    }

    [HttpPost]
    [Authorize(Policy = "accounting.opening_balances.post")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> PostOpeningBalance(
        [FromBody] CreateOpeningBalanceRequestDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnforceLineLocationAccessAsync(dto.Lines);

        var journalId = await _sender.Send(
            new PostOpeningBalanceCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                new OpeningBalanceInput(
                    dto.AccountingStartDate,
                    dto.ReferenceNumber,
                    dto.Description,
                    dto.Lines?.Select(line => new OpeningBalanceLineInput(
                        line.AccountId,
                        line.DebitAmount,
                        line.CreditAmount,
                        line.OutletId,
                        line.WarehouseId,
                        line.Description)).ToArray())),
            cancellationToken);
        var posted = JournalEntryReadModelMapper.ToDto(await _sender.Send(
            new GetJournalEntryByIdQuery(_tenantAccess.RequireBusinessId(), journalId),
            cancellationToken));
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(posted, "Opening balance posted successfully"));
    }

    [HttpPost("{openingBalanceJournalEntryId:long}/adjustments")]
    [Authorize(Policy = "accounting.opening_balances.adjust")]
    public async Task<ActionResult<ApiResponse<JournalEntryDto>>> PostAdjustment(
        long openingBalanceJournalEntryId,
        [FromBody] CreateOpeningBalanceAdjustmentRequestDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<JournalEntryDto>.ErrorResponse("User identity not found"));
        }

        await EnforceLineLocationAccessAsync(dto.Lines);

        var journalId = await _sender.Send(
            new PostOpeningBalanceAdjustmentCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                openingBalanceJournalEntryId,
                new OpeningBalanceAdjustmentInput(
                    dto.Reason,
                    dto.AdjustmentBusinessDate,
                    dto.PostingDate,
                    dto.ReferenceNumber,
                    dto.Description,
                    dto.Lines?.Select(line => new OpeningBalanceLineInput(
                        line.AccountId,
                        line.DebitAmount,
                        line.CreditAmount,
                        line.OutletId,
                        line.WarehouseId,
                        line.Description)).ToArray())),
            cancellationToken);
        var posted = JournalEntryReadModelMapper.ToDto(await _sender.Send(
            new GetJournalEntryByIdQuery(_tenantAccess.RequireBusinessId(), journalId),
            cancellationToken));
        return Ok(ApiResponse<JournalEntryDto>.SuccessResponse(posted, "Opening balance adjustment posted successfully"));
    }

    private async Task EnforceLineLocationAccessAsync(IEnumerable<OpeningBalanceLineDto>? lines)
    {
        if (lines == null)
        {
            return;
        }

        foreach (var line in lines)
        {
            if (line.OutletId.HasValue && line.WarehouseId.HasValue)
            {
                throw new InvalidOperationException("Opening balance line cannot target both outlet and warehouse dimensions at the same time.");
            }

            if (line.OutletId.HasValue)
            {
                var resolved = await _outletAccessService.EnforceWriteLocationAsync(line.OutletId.Value, "outlet");
                line.OutletId = resolved.LocationId;
            }

            if (line.WarehouseId.HasValue)
            {
                var resolved = await _outletAccessService.EnforceWriteLocationAsync(line.WarehouseId.Value, "warehouse");
                line.WarehouseId = resolved.LocationId;
            }
        }
    }

    private bool TryGetCurrentUserId(out long userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(value, out userId);
    }
}
