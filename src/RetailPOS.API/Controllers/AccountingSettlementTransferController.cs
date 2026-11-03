using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.AccountingSettlements.Commands;
using RetailPOS.Application.AccountingSettlements.Queries;
using RetailPOS.Application.AccountingSettlements.ReadModels;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Models;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting")]
[Authorize]
public sealed class AccountingSettlementTransferController : ControllerBase
{
    private readonly ISender _sender;

    public AccountingSettlementTransferController(ISender sender) => _sender = sender;

    [HttpGet("settlements")]
    [Authorize(Policy = "accounting.settlements.view")]
    public async Task<ActionResult<ApiResponse<List<SettlementBatchDto>>>> ListSettlements(
        CancellationToken cancellationToken)
    {
        var rows = await _sender.Send(new ListSettlementBatchesQuery(), cancellationToken);
        return Ok(ApiResponse<List<SettlementBatchDto>>.SuccessResponse(
            rows.Select(Map).ToList()));
    }

    [HttpGet("settlements/{id:long}")]
    [Authorize(Policy = "accounting.settlements.view")]
    public async Task<ActionResult<ApiResponse<SettlementBatchDto>>> GetSettlement(
        long id,
        CancellationToken cancellationToken)
    {
        var row = await _sender.Send(new GetSettlementBatchQuery(id), cancellationToken);
        return row is null
            ? NotFound()
            : Ok(ApiResponse<SettlementBatchDto>.SuccessResponse(Map(row)));
    }

    [HttpPost("settlements")]
    [Authorize(Policy = "accounting.settlements.post")]
    public async Task<ActionResult<ApiResponse<SettlementBatchDto>>> PostSettlement(
        CreateSettlementBatchDto dto,
        CancellationToken cancellationToken)
    {
        var row = await _sender.Send(
            new PostSettlementCommand(
                CurrentUserId(),
                new RetailPOS.Application.AccountingSettlements.Abstractions.SettlementPostingInput(
                    dto.SettlementType,
                    dto.ClearingAccountId,
                    dto.BankAccountId,
                    dto.SettlementDate,
                    dto.GrossClearingAmount,
                    dto.SettlementFeeAmount,
                    dto.ExternalReference,
                    dto.IdempotencyKey)),
            cancellationToken);
        return Ok(ApiResponse<SettlementBatchDto>.SuccessResponse(
            Map(row),
            "Settlement posted successfully"));
    }

    [HttpPost("settlements/{id:long}/reverse")]
    [Authorize(Policy = "accounting.settlements.reverse")]
    public async Task<ActionResult<ApiResponse<SettlementBatchDto>>> ReverseSettlement(
        long id,
        ReverseSettlementBatchDto dto,
        CancellationToken cancellationToken)
    {
        var row = await _sender.Send(
            new ReverseSettlementCommand(
                id,
                CurrentUserId(),
                new RetailPOS.Application.AccountingSettlements.Abstractions.SettlementReversalInput(
                    dto.Reason,
                    dto.PostingDate)),
            cancellationToken);
        return Ok(ApiResponse<SettlementBatchDto>.SuccessResponse(
            Map(row),
            "Settlement reversed successfully"));
    }

    [HttpGet("liquidity-transfers")]
    [Authorize(Policy = "accounting.liquidity_transfers.view")]
    public async Task<ActionResult<ApiResponse<List<LiquidityTransferDto>>>> ListTransfers(
        CancellationToken cancellationToken)
    {
        var rows = await _sender.Send(new ListLiquidityTransfersQuery(), cancellationToken);
        return Ok(ApiResponse<List<LiquidityTransferDto>>.SuccessResponse(
            rows.Select(Map).ToList()));
    }

    [HttpGet("liquidity-transfers/{id:long}")]
    [Authorize(Policy = "accounting.liquidity_transfers.view")]
    public async Task<ActionResult<ApiResponse<LiquidityTransferDto>>> GetTransfer(
        long id,
        CancellationToken cancellationToken)
    {
        var row = await _sender.Send(new GetLiquidityTransferQuery(id), cancellationToken);
        return row is null
            ? NotFound()
            : Ok(ApiResponse<LiquidityTransferDto>.SuccessResponse(Map(row)));
    }

    [HttpPost("liquidity-transfers")]
    [Authorize(Policy = "accounting.liquidity_transfers.post")]
    public async Task<ActionResult<ApiResponse<LiquidityTransferDto>>> PostTransfer(
        CreateLiquidityTransferDto dto,
        CancellationToken cancellationToken)
    {
        var row = await _sender.Send(
            new PostLiquidityTransferCommand(
                CurrentUserId(),
                new RetailPOS.Application.AccountingSettlements.Abstractions.LiquidityTransferPostingInput(
                    dto.TransferType,
                    dto.SourceBankAccountId,
                    dto.DestinationBankAccountId,
                    dto.Amount,
                    dto.PostingDate,
                    dto.Reference,
                    dto.IdempotencyKey)),
            cancellationToken);
        return Ok(ApiResponse<LiquidityTransferDto>.SuccessResponse(
            Map(row),
            "Liquidity transfer posted successfully"));
    }

    [HttpPost("liquidity-transfers/{id:long}/reverse")]
    [Authorize(Policy = "accounting.liquidity_transfers.reverse")]
    public async Task<ActionResult<ApiResponse<LiquidityTransferDto>>> ReverseTransfer(
        long id,
        ReverseLiquidityTransferDto dto,
        CancellationToken cancellationToken)
    {
        var row = await _sender.Send(
            new ReverseLiquidityTransferCommand(
                id,
                CurrentUserId(),
                new RetailPOS.Application.AccountingSettlements.Abstractions.LiquidityTransferReversalInput(
                    dto.Reason,
                    dto.PostingDate)),
            cancellationToken);
        return Ok(ApiResponse<LiquidityTransferDto>.SuccessResponse(
            Map(row),
            "Liquidity transfer reversed successfully"));
    }

    private long CurrentUserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException("User identity not found.");

    private static SettlementBatchDto Map(SettlementBatchReadModel readModel) => new()
    {
        Id = readModel.Id,
        BusinessId = readModel.BusinessId,
        SettlementType = readModel.SettlementType,
        ClearingAccountId = readModel.ClearingAccountId,
        BankAccountId = readModel.BankAccountId,
        SettlementDate = readModel.SettlementDate,
        GrossClearingAmount = readModel.GrossClearingAmount,
        SettlementFeeAmount = readModel.SettlementFeeAmount,
        NetBankAmount = readModel.NetBankAmount,
        ExternalReference = readModel.ExternalReference,
        IdempotencyKey = readModel.IdempotencyKey,
        Status = readModel.Status,
        JournalEntryId = readModel.JournalEntryId,
        ReversalJournalEntryId = readModel.ReversalJournalEntryId
    };

    private static LiquidityTransferDto Map(LiquidityTransferReadModel readModel) => new()
    {
        Id = readModel.Id,
        BusinessId = readModel.BusinessId,
        TransferType = readModel.TransferType,
        SourceAccountId = readModel.SourceAccountId,
        DestinationAccountId = readModel.DestinationAccountId,
        SourceBankAccountId = readModel.SourceBankAccountId,
        DestinationBankAccountId = readModel.DestinationBankAccountId,
        Amount = readModel.Amount,
        PostingDate = readModel.PostingDate,
        Reference = readModel.Reference,
        IdempotencyKey = readModel.IdempotencyKey,
        Status = readModel.Status,
        JournalEntryId = readModel.JournalEntryId,
        ReversalJournalEntryId = readModel.ReversalJournalEntryId
    };
}
