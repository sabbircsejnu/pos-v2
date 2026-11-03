using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.API.DTOs.SupplierPayment;
using RetailPOS.API.Models;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/supplier-payments")]
public class AccountingSupplierPaymentsController : ControllerBase
{
    private readonly ISender _sender;

    public AccountingSupplierPaymentsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [Authorize(Policy = "accounting.supplier_payments.create")]
    public async Task<ActionResult<ApiResponse<SupplierPaymentDto>>> CreatePayment(
        [FromBody] CreateSupplierPaymentDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<SupplierPaymentDto>.ErrorResponse("User identity not found"));
        }

        var readModel = await _sender.Send(
            new CreateSupplierPaymentCommand(
                dto.SupplierId,
                dto.OutletId,
                dto.PaymentDate,
                dto.PaymentMethod,
                dto.PaymentAccountId,
                dto.Amount,
                dto.ReferenceNumber,
                dto.Notes,
                dto.Allocations?.Select(line => new CreateSupplierPaymentAllocationCommand(
                    line.BillId,
                    line.SupplierPayableDocumentId,
                    line.Amount)).ToArray(),
                actorUserId),
            cancellationToken);
        var result = new SupplierPaymentDto
        {
            Id = readModel.Id,
            SupplierId = readModel.SupplierId,
            SupplierName = readModel.SupplierName,
            OutletId = readModel.OutletId,
            PaymentDate = readModel.PaymentDate,
            PostingDate = readModel.PostingDate,
            PaymentMethod = readModel.PaymentMethod,
            PaymentAccountId = readModel.PaymentAccountId,
            Amount = readModel.Amount,
            AllocatedAmount = readModel.AllocatedAmount,
            AdvanceAmount = readModel.AdvanceAmount,
            Status = readModel.Status,
            ReferenceNumber = readModel.ReferenceNumber,
            Notes = readModel.Notes,
            JournalEntryId = readModel.JournalEntryId
        };
        return Created(string.Empty, ApiResponse<SupplierPaymentDto>.SuccessResponse(result, "Supplier payment created successfully"));
    }

    [HttpPost("{id:long}/submit")]
    [Authorize(Policy = "accounting.supplier_payments.submit")]
    public async Task<ActionResult<ApiResponse<SupplierPaymentDto>>> SubmitPayment(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<SupplierPaymentDto>.ErrorResponse("User identity not found"));
        }

        var readModel = await _sender.Send(
            new SubmitSupplierPaymentCommand(id, actorUserId),
            cancellationToken);
        var result = new SupplierPaymentDto
        {
            Id = readModel.Id,
            SupplierId = readModel.SupplierId,
            SupplierName = readModel.SupplierName,
            OutletId = readModel.OutletId,
            PaymentDate = readModel.PaymentDate,
            PostingDate = readModel.PostingDate,
            PaymentMethod = readModel.PaymentMethod,
            PaymentAccountId = readModel.PaymentAccountId,
            Amount = readModel.Amount,
            AllocatedAmount = readModel.AllocatedAmount,
            AdvanceAmount = readModel.AdvanceAmount,
            Status = readModel.Status,
            ReferenceNumber = readModel.ReferenceNumber,
            Notes = readModel.Notes,
            JournalEntryId = readModel.JournalEntryId
        };
        return Ok(ApiResponse<SupplierPaymentDto>.SuccessResponse(result, "Supplier payment submitted successfully"));
    }

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = "accounting.supplier_payments.approve")]
    public async Task<ActionResult<ApiResponse<SupplierPaymentDto>>> ApprovePayment(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<SupplierPaymentDto>.ErrorResponse("User identity not found"));
        }

        var readModel = await _sender.Send(
            new ApproveSupplierPaymentCommand(id, actorUserId),
            cancellationToken);
        var result = new SupplierPaymentDto
        {
            Id = readModel.Id,
            SupplierId = readModel.SupplierId,
            SupplierName = readModel.SupplierName,
            OutletId = readModel.OutletId,
            PaymentDate = readModel.PaymentDate,
            PostingDate = readModel.PostingDate,
            PaymentMethod = readModel.PaymentMethod,
            PaymentAccountId = readModel.PaymentAccountId,
            Amount = readModel.Amount,
            AllocatedAmount = readModel.AllocatedAmount,
            AdvanceAmount = readModel.AdvanceAmount,
            Status = readModel.Status,
            ReferenceNumber = readModel.ReferenceNumber,
            Notes = readModel.Notes,
            JournalEntryId = readModel.JournalEntryId
        };
        return Ok(ApiResponse<SupplierPaymentDto>.SuccessResponse(result, "Supplier payment approved successfully"));
    }

    [HttpPost("{id:long}/reverse")]
    [Authorize(Policy = "accounting.supplier_payments.cancel")]
    public async Task<ActionResult<ApiResponse<SupplierPaymentDto>>> ReversePayment(
        long id,
        [FromBody] ReverseSupplierPaymentDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
            return Unauthorized(ApiResponse<SupplierPaymentDto>.ErrorResponse("User identity not found"));

        var readModel = await _sender.Send(
            new ReverseSupplierPaymentCommand(id, dto.PostingDate, dto.Reason, actorUserId),
            cancellationToken);
        var result = new SupplierPaymentDto
        {
            Id = readModel.Id,
            SupplierId = readModel.SupplierId,
            SupplierName = readModel.SupplierName,
            OutletId = readModel.OutletId,
            PaymentDate = readModel.PaymentDate,
            PostingDate = readModel.PostingDate,
            PaymentMethod = readModel.PaymentMethod,
            PaymentAccountId = readModel.PaymentAccountId,
            Amount = readModel.Amount,
            AllocatedAmount = readModel.AllocatedAmount,
            AdvanceAmount = readModel.AdvanceAmount,
            Status = readModel.Status,
            ReferenceNumber = readModel.ReferenceNumber,
            Notes = readModel.Notes,
            JournalEntryId = readModel.JournalEntryId
        };
        return Ok(ApiResponse<SupplierPaymentDto>.SuccessResponse(result, "Supplier payment reversed successfully"));
    }

    [HttpPost("advances")]
    [Authorize(Policy = "accounting.supplier_advances.create")]
    public async Task<ActionResult<ApiResponse<SupplierAdvanceDto>>> CreateAdvance(
        [FromBody] CreateSupplierAdvanceDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<SupplierAdvanceDto>.ErrorResponse("User identity not found"));
        }

        var readModel = await _sender.Send(
            new CreateSupplierAdvanceCommand(
                dto.SupplierId,
                dto.OutletId,
                dto.PaymentDate,
                dto.PaymentMethod,
                dto.PaymentAccountId,
                dto.Amount,
                dto.ReferenceNumber,
                dto.Notes,
                actorUserId),
            cancellationToken);
        var result = new SupplierAdvanceDto
        {
            Id = readModel.Id,
            SupplierId = readModel.SupplierId,
            SupplierName = readModel.SupplierName,
            OutletId = readModel.OutletId,
            PaymentDate = readModel.PaymentDate,
            PaymentMethod = readModel.PaymentMethod,
            PaymentAccountId = readModel.PaymentAccountId,
            Amount = readModel.Amount,
            RemainingAmount = readModel.RemainingAmount,
            AppliedAmount = readModel.AppliedAmount,
            Status = readModel.Status,
            ReferenceNumber = readModel.ReferenceNumber,
            Notes = readModel.Notes,
            JournalEntryId = readModel.JournalEntryId
        };
        return Created(string.Empty, ApiResponse<SupplierAdvanceDto>.SuccessResponse(result, "Supplier advance created successfully"));
    }

    [HttpPost("advances/{id:long}/submit")]
    [Authorize(Policy = "accounting.supplier_advances.submit")]
    public async Task<ActionResult<ApiResponse<SupplierAdvanceDto>>> SubmitAdvance(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<SupplierAdvanceDto>.ErrorResponse("User identity not found"));
        }

        var readModel = await _sender.Send(
            new SubmitSupplierAdvanceCommand(id, actorUserId),
            cancellationToken);
        var result = new SupplierAdvanceDto
        {
            Id = readModel.Id,
            SupplierId = readModel.SupplierId,
            SupplierName = readModel.SupplierName,
            OutletId = readModel.OutletId,
            PaymentDate = readModel.PaymentDate,
            PaymentMethod = readModel.PaymentMethod,
            PaymentAccountId = readModel.PaymentAccountId,
            Amount = readModel.Amount,
            RemainingAmount = readModel.RemainingAmount,
            AppliedAmount = readModel.AppliedAmount,
            Status = readModel.Status,
            ReferenceNumber = readModel.ReferenceNumber,
            Notes = readModel.Notes,
            JournalEntryId = readModel.JournalEntryId
        };
        return Ok(ApiResponse<SupplierAdvanceDto>.SuccessResponse(result, "Supplier advance submitted successfully"));
    }

    [HttpPost("advances/{id:long}/approve")]
    [Authorize(Policy = "accounting.supplier_advances.approve")]
    public async Task<ActionResult<ApiResponse<SupplierAdvanceDto>>> ApproveAdvance(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<SupplierAdvanceDto>.ErrorResponse("User identity not found"));
        }

        var readModel = await _sender.Send(
            new ApproveSupplierAdvanceCommand(id, actorUserId),
            cancellationToken);
        var result = MapAdvance(readModel);
        return Ok(ApiResponse<SupplierAdvanceDto>.SuccessResponse(result, "Supplier advance approved successfully"));
    }

    [HttpPost("advances/{id:long}/utilizations")]
    [Authorize(Policy = "accounting.supplier_advances.approve")]
    public async Task<ActionResult<ApiResponse>> ApplyAdvance(
        long id,
        [FromBody] ApplySupplierAdvanceDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId)) return Unauthorized(ApiResponse.ErrorResponse("User identity not found"));
        await _sender.Send(
            new ApplySupplierAdvanceCommand(
                id,
                dto.SupplierPayableDocumentId,
                dto.Amount,
                dto.EffectiveDate,
                dto.ReferenceNumber,
                dto.Notes,
                actorUserId),
            cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Supplier advance applied successfully"));
    }

    [HttpPost("advance-utilizations/{id:long}/reverse")]
    [Authorize(Policy = "accounting.supplier_advances.cancel")]
    public async Task<ActionResult<ApiResponse>> ReverseAdvanceUtilization(
        long id,
        [FromBody] ReverseSupplierAdvanceUtilizationDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId)) return Unauthorized(ApiResponse.ErrorResponse("User identity not found"));
        await _sender.Send(
            new ReverseSupplierAdvanceUtilizationCommand(
                id,
                dto.PostingDate,
                dto.Reason,
                actorUserId),
            cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Supplier advance utilization reversed successfully"));
    }

    private bool TryGetCurrentUserId(out long userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(value, out userId);
    }

    private static SupplierAdvanceDto MapAdvance(SupplierAdvanceReadModel readModel) => new()
    {
        Id = readModel.Id,
        SupplierId = readModel.SupplierId,
        SupplierName = readModel.SupplierName,
        OutletId = readModel.OutletId,
        PaymentDate = readModel.PaymentDate,
        PaymentMethod = readModel.PaymentMethod,
        PaymentAccountId = readModel.PaymentAccountId,
        Amount = readModel.Amount,
        RemainingAmount = readModel.RemainingAmount,
        AppliedAmount = readModel.AppliedAmount,
        Status = readModel.Status,
        ReferenceNumber = readModel.ReferenceNumber,
        Notes = readModel.Notes,
        JournalEntryId = readModel.JournalEntryId
    };
}
