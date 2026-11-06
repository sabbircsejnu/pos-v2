using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Microsoft.Net.Http.Headers;
using RetailPOS.API.Authorization;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.Expense;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Expenses.Commands;
using RetailPOS.Application.Expenses.Queries;
using RetailPOS.Application.Identity.Permissions;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/accounting/expenses")]
public class AccountingExpensesController : ControllerBase
{
    private readonly ITenantAccessService _tenantAccess;
    private readonly ISender _sender;

    public AccountingExpensesController(
        ITenantAccessService tenantAccess,
        ISender sender)
    {
        _tenantAccess = tenantAccess;
        _sender = sender;
    }

    /// <summary>
    /// Funding accounts selectable on an expense; needs no Chart of Accounts permission. Pass the payment source and
    /// location to get only what that source may credit there.
    /// </summary>
    [HttpGet("payment-accounts")]
    [Authorize(Policy = LookupAuthorizationPolicies.ExpenseEntryWrite)]
    public async Task<ActionResult<ApiResponse<List<ExpensePaymentAccountDto>>>> GetPaymentAccounts(
        [FromQuery] string? paymentSource,
        [FromQuery] long? outletId,
        [FromQuery] long? warehouseId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetExpensePaymentAccountsQuery(_tenantAccess.RequireBusinessId(), paymentSource, outletId, warehouseId),
            cancellationToken);
        return Ok(ApiResponse<List<ExpensePaymentAccountDto>>.SuccessResponse(result.Select(ExpenseReadModelMapper.ToDto).ToList()));
    }

    /// <summary>Ledger accounts a category may be mapped to, for category managers who need no Chart of Accounts access.</summary>
    [HttpGet("expense-accounts")]
    [Authorize(Policy = PermissionCatalog.ExpenseCategoriesManage)]
    public async Task<ActionResult<ApiResponse<List<ExpenseAccountOptionDto>>>> GetExpenseAccounts(
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetEligibleExpenseAccountsQuery(_tenantAccess.RequireBusinessId()),
            cancellationToken);
        return Ok(ApiResponse<List<ExpenseAccountOptionDto>>.SuccessResponse(result.Select(ExpenseReadModelMapper.ToDto).ToList()));
    }

    [HttpGet("{id:long}/attachments")]
    [Authorize(Policy = LookupAuthorizationPolicies.ExpenseEntryRead)]
    public async Task<ActionResult<ApiResponse<List<ExpenseAttachmentDto>>>> GetAttachments(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetExpenseAttachmentsQuery(_tenantAccess.RequireBusinessId(), id),
            cancellationToken);
        return Ok(ApiResponse<List<ExpenseAttachmentDto>>.SuccessResponse(result.Select(ExpenseReadModelMapper.ToDto).ToList()));
    }

    public sealed class UploadExpenseAttachmentRequest
    {
        public IFormFile File { get; set; } = default!;
    }

    [HttpPost("{id:long}/attachments")]
    [Authorize(Policy = LookupAuthorizationPolicies.ExpenseEntryWrite)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<ExpenseAttachmentDto>>> UploadAttachment(
        long id,
        [FromForm] UploadExpenseAttachmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<ExpenseAttachmentDto>.ErrorResponse("User identity not found"));
        }

        await using var content = request.File.OpenReadStream();
        var result = await _sender.Send(
            new UploadExpenseAttachmentCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id,
                content,
                request.File.FileName),
            cancellationToken);
        return Ok(ApiResponse<ExpenseAttachmentDto>.SuccessResponse(ExpenseReadModelMapper.ToDto(result), "Attachment uploaded"));
    }

    /// <summary>Streams an attachment through authorization; <paramref name="inline"/> asks the browser to preview it.</summary>
    [HttpGet("{id:long}/attachments/{attachmentId:long}/content")]
    [Authorize(Policy = LookupAuthorizationPolicies.ExpenseEntryRead)]
    public async Task<IActionResult> GetAttachmentContent(
        long id,
        long attachmentId,
        [FromQuery] bool inline = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new GetExpenseAttachmentContentQuery(_tenantAccess.RequireBusinessId(), id, attachmentId),
            cancellationToken);

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        disposition.SetHttpFileName(result.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        return File(result.Content, result.ContentType);
    }

    [HttpDelete("{id:long}/attachments/{attachmentId:long}")]
    [Authorize(Policy = LookupAuthorizationPolicies.ExpenseEntryWrite)]
    public async Task<ActionResult<ApiResponse>> DeleteAttachment(
        long id,
        long attachmentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse.ErrorResponse("User identity not found"));
        }

        await _sender.Send(
            new DeleteExpenseAttachmentCommand(_tenantAccess.RequireBusinessId(), actorUserId, id, attachmentId),
            cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Attachment removed"));
    }

    [HttpGet("categories")]
    [Authorize(Policy = LookupAuthorizationPolicies.ExpenseCategoryRead)]
    public async Task<ActionResult<ApiResponse<List<ExpenseCategoryDto>>>> GetCategories(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new GetExpenseCategoriesQuery(_tenantAccess.RequireBusinessId(), includeInactive),
            cancellationToken);
        return Ok(ApiResponse<List<ExpenseCategoryDto>>.SuccessResponse(result.Select(ExpenseReadModelMapper.ToDto).ToList()));
    }

    [HttpPost("categories")]
    [Authorize(Policy = PermissionCatalog.ExpenseCategoriesManage)]
    public async Task<ActionResult<ApiResponse<ExpenseCategoryDto>>> CreateCategory(
        [FromBody] UpsertExpenseCategoryDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<ExpenseCategoryDto>.ErrorResponse("User identity not found"));
        }

        var created = await _sender.Send(
            new CreateExpenseCategoryCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                new ExpenseCategoryCommandInput(
                    dto.Name,
                    dto.ExpenseAccountId,
                    dto.IsActive)),
            cancellationToken);
        return Ok(ApiResponse<ExpenseCategoryDto>.SuccessResponse(
            new ExpenseCategoryDto
            {
                Id = created.Id,
                Name = created.Name,
                ExpenseAccountId = created.ExpenseAccountId,
                ExpenseAccountCode = created.ExpenseAccountCode,
                ExpenseAccountName = created.ExpenseAccountName,
                IsActive = created.IsActive
            },
            "Expense category created successfully"));
    }

    [HttpPut("categories/{id:long}")]
    [Authorize(Policy = PermissionCatalog.ExpenseCategoriesManage)]
    public async Task<ActionResult<ApiResponse<ExpenseCategoryDto>>> UpdateCategory(
        long id,
        [FromBody] UpsertExpenseCategoryDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<ExpenseCategoryDto>.ErrorResponse("User identity not found"));
        }

        var updated = await _sender.Send(
            new UpdateExpenseCategoryCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id,
                new ExpenseCategoryCommandInput(
                    dto.Name,
                    dto.ExpenseAccountId,
                    dto.IsActive)),
            cancellationToken);
        return Ok(ApiResponse<ExpenseCategoryDto>.SuccessResponse(
            new ExpenseCategoryDto
            {
                Id = updated.Id,
                Name = updated.Name,
                ExpenseAccountId = updated.ExpenseAccountId,
                ExpenseAccountCode = updated.ExpenseAccountCode,
                ExpenseAccountName = updated.ExpenseAccountName,
                IsActive = updated.IsActive
            },
            "Expense category updated successfully"));
    }

    [HttpGet]
    [Authorize(Policy = "accounting.expenses.view")]
    public async Task<ActionResult<ApiResponse<ExpenseListDto>>> Search([FromQuery] string? category, [FromQuery] string? status,
        [FromQuery] long? outletId, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        [FromQuery] long? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new SearchExpensesQuery(new ExpenseSearchFilter(
                _tenantAccess.RequireBusinessId(),
                category,
                status,
                outletId,
                startDate,
                endDate,
                pageNumber,
                pageSize,
                warehouseId)),
            cancellationToken);

        return Ok(ApiResponse<ExpenseListDto>.SuccessResponse(ExpenseReadModelMapper.ToDto(result)));
    }

    [HttpPost("search")]
    [Authorize(Policy = "accounting.expenses.view")]
    public async Task<ActionResult<ApiResponse<ExpenseListDto>>> SearchPost(
        [FromBody] ExpenseSearchDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SearchExpensesQuery(new ExpenseSearchFilter(
                _tenantAccess.RequireBusinessId(),
                dto.Category,
                dto.Status,
                dto.OutletId,
                dto.StartDate,
                dto.EndDate,
                dto.PageNumber,
                dto.PageSize,
                dto.WarehouseId)),
            cancellationToken);
        return Ok(ApiResponse<ExpenseListDto>.SuccessResponse(ExpenseReadModelMapper.ToDto(result)));
    }

    [HttpGet("{id:long}")]
    [Authorize(Policy = "accounting.expenses.view")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await GetExpenseDtoAsync(id, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(result));
    }

    [HttpPost]
    [Authorize(Policy = "accounting.expenses.create")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Create(
        [FromBody] CreateExpenseDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<ExpenseDto>.ErrorResponse("User identity not found"));
        }

        var expenseId = await _sender.Send(
            new CreateExpenseCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                new ExpenseCreationInput(
                    dto.ExpenseCategoryId,
                    dto.Amount,
                    dto.VatRate,
                    dto.VatAmount,
                    dto.TaxCode,
                    dto.TaxInclusive,
                    dto.PaymentMethodId,
                    dto.PaymentAccountId,
                    dto.ReferenceNumber,
                    dto.Description,
                    dto.AttachmentPath,
                    dto.ExpenseDate,
                    dto.OutletId,
                    dto.PaymentSource,
                    dto.WarehouseId)),
            cancellationToken);
        var result = await GetExpenseDtoAsync(expenseId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<ExpenseDto>.SuccessResponse(result, "Expense created successfully"));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "accounting.expenses.edit")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Update(
        long id,
        [FromBody] UpdateExpenseDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<ExpenseDto>.ErrorResponse("User identity not found"));
        }

        var expenseId = await _sender.Send(
            new UpdateExpenseCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id,
                new ExpenseUpdateInput(
                    dto.ExpenseCategoryId,
                    dto.Amount,
                    dto.VatRate,
                    dto.VatAmount,
                    dto.TaxCode,
                    dto.TaxInclusive,
                    dto.PaymentMethodId,
                    dto.PaymentAccountId,
                    dto.ReferenceNumber,
                    dto.Description,
                    dto.AttachmentPath,
                    dto.ExpenseDate,
                    dto.OutletId,
                    dto.PaymentSource,
                    dto.WarehouseId)),
            cancellationToken);
        var result = await GetExpenseDtoAsync(expenseId, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(result, "Expense updated successfully"));
    }

    public sealed class DrawerExpenseActionDto
    {
        [Required] public long TerminalId { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Records the physical cash payout of an approved POS Drawer expense on the open session of the paying terminal.
    /// Approval alone never moves drawer cash; retries are idempotent.
    /// </summary>
    [HttpPost("{id:long}/pay-from-drawer")]
    [Authorize(Policy = "cash_sessions.expenses.pay")]
    public async Task<ActionResult<ApiResponse<DrawerExpenseDisbursementResult>>> PayFromDrawer(
        long id,
        [FromBody] DrawerExpenseActionDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
            return Unauthorized(ApiResponse<DrawerExpenseDisbursementResult>.ErrorResponse("User identity not found"));

        var result = await _sender.Send(
            new PayDrawerExpenseCommand(_tenantAccess.RequireBusinessId(), actorUserId, id, dto.TerminalId),
            cancellationToken);
        return Ok(ApiResponse<DrawerExpenseDisbursementResult>.SuccessResponse(result, "Drawer expense paid."));
    }

    [HttpPost("{id:long}/reverse-drawer-payment")]
    [Authorize(Policy = "cash_sessions.expenses.pay")]
    public async Task<ActionResult<ApiResponse<DrawerExpenseDisbursementResult>>> ReverseDrawerPayment(
        long id,
        [FromBody] DrawerExpenseActionDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
            return Unauthorized(ApiResponse<DrawerExpenseDisbursementResult>.ErrorResponse("User identity not found"));

        var result = await _sender.Send(
            new ReverseDrawerExpenseCommand(_tenantAccess.RequireBusinessId(), actorUserId, id, dto.TerminalId, dto.Reason ?? string.Empty),
            cancellationToken);
        return Ok(ApiResponse<DrawerExpenseDisbursementResult>.SuccessResponse(result, "Drawer payment reversed."));
    }

    [HttpPost("{id:long}/submit")]
    [Authorize(Policy = "accounting.expenses.submit")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Submit(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<ExpenseDto>.ErrorResponse("User identity not found"));
        }

        var expenseId = await _sender.Send(
            new SubmitExpenseCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id),
            cancellationToken);
        var result = await GetExpenseDtoAsync(expenseId, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(result, "Expense submitted successfully"));
    }

    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = "accounting.expenses.approve")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Approve(
        long id,
        [FromBody] ApproveExpenseDto? dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<ExpenseDto>.ErrorResponse("User identity not found"));
        }

        var expenseId = await _sender.Send(
            new ApproveExpenseCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id,
                dto?.ExpenseAccountId),
            cancellationToken);
        var result = await GetExpenseDtoAsync(expenseId, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(result, "Expense approved successfully"));
    }

    [HttpPost("{id:long}/reject")]
    [Authorize(Policy = "accounting.expenses.reject")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Reject(
        long id,
        [FromBody] RequiredReasonActionDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<ExpenseDto>.ErrorResponse("User identity not found"));
        }

        var expenseId = await _sender.Send(
            new RejectExpenseCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id,
                dto.Reason),
            cancellationToken);
        var result = await GetExpenseDtoAsync(expenseId, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(result, "Expense rejected successfully"));
    }

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = "accounting.expenses.cancel")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Cancel(
        long id,
        [FromBody] RequiredReasonActionDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse<ExpenseDto>.ErrorResponse("User identity not found"));
        }

        var expenseId = await _sender.Send(
            new CancelExpenseCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id,
                dto.Reason),
            cancellationToken);
        var result = await GetExpenseDtoAsync(expenseId, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(result, "Expense cancelled successfully"));
    }

    [HttpGet("summary")]
    [Authorize(Policy = "accounting.expenses.view")]
    public async Task<ActionResult<ApiResponse<ExpenseSummaryDto>>> GetSummary(
        CancellationToken cancellationToken)
    {
        var readModel = await _sender.Send(
            new GetCurrentMonthExpenseSummaryQuery(_tenantAccess.RequireBusinessId()),
            cancellationToken);
        return Ok(ApiResponse<ExpenseSummaryDto>.SuccessResponse(ExpenseReadModelMapper.ToDto(readModel)));
    }

    private bool TryGetCurrentUserId(out long userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return long.TryParse(value, out userId);
    }

    private async Task<ExpenseDto> GetExpenseDtoAsync(long id, CancellationToken cancellationToken)
    {
        var readModel = await _sender.Send(
            new GetExpenseByIdQuery(_tenantAccess.RequireBusinessId(), id),
            cancellationToken);
        return ExpenseReadModelMapper.ToDto(readModel);
    }
}