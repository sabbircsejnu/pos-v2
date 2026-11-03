using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using MediatR;
using RetailPOS.API.DTOs.Expense;
using RetailPOS.API.Models;
using RetailPOS.API.Services;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Expenses.Commands;
using RetailPOS.Application.Expenses.Queries;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "expenses.view")]
public class ExpensesController : ControllerBase
{
    private readonly ILogger<ExpensesController> _logger;
    private readonly ITenantAccessService _tenantAccess;
    private readonly ISender _sender;

    public ExpensesController(
        ILogger<ExpensesController> logger,
        ITenantAccessService tenantAccess,
        ISender sender)
    {
        _logger = logger;
        _tenantAccess = tenantAccess;
        _sender = sender;
    }

    /// <summary>List expenses with optional filters</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<ExpenseListDto>>> GetAll(
        [FromQuery] string? category,
        [FromQuery] long? outletId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SearchExpensesQuery(new ExpenseSearchFilter(
                _tenantAccess.RequireBusinessId(),
                category,
                null,
                outletId,
                startDate,
                endDate,
                1,
                20)),
            cancellationToken);
        return Ok(ApiResponse<ExpenseListDto>.SuccessResponse(ExpenseReadModelMapper.ToDto(result)));
    }

    /// <summary>Paginated expense search</summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<ExpenseListDto>>> Search(
        [FromBody] ExpenseSearchDto searchDto,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SearchExpensesQuery(new ExpenseSearchFilter(
                _tenantAccess.RequireBusinessId(),
                searchDto.Category,
                searchDto.Status,
                searchDto.OutletId,
                searchDto.StartDate,
                searchDto.EndDate,
                searchDto.PageNumber,
                searchDto.PageSize)),
            cancellationToken);
        return Ok(ApiResponse<ExpenseListDto>.SuccessResponse(ExpenseReadModelMapper.ToDto(result)));
    }

    /// <summary>Get expense by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var expense = await GetExpenseDtoAsync(id, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(expense));
    }

    /// <summary>Create a new expense</summary>
    [HttpPost]
    [Authorize(Policy = "expenses.create")]
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
                    dto.OutletId)),
            cancellationToken);
        var expense = await GetExpenseDtoAsync(expenseId, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = expense.Id },
            ApiResponse<ExpenseDto>.SuccessResponse(expense, "Expense created successfully"));
    }

    /// <summary>Update an expense</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "expenses.edit")]
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
                    dto.OutletId)),
            cancellationToken);
        var expense = await GetExpenseDtoAsync(expenseId, cancellationToken);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(expense, "Expense updated successfully"));
    }

    /// <summary>Delete an expense</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "expenses.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var actorUserId))
        {
            return Unauthorized(ApiResponse.ErrorResponse("User identity not found"));
        }

        await _sender.Send(
            new DeleteExpenseCommand(
                _tenantAccess.RequireBusinessId(),
                actorUserId,
                id),
            cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Expense deleted successfully"));
    }

    /// <summary>Get expense summary grouped by category for the current month</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<ExpenseSummaryDto>>> GetSummary(
        CancellationToken cancellationToken)
    {
        var summary = await _sender.Send(
            new GetCurrentMonthExpenseSummaryQuery(_tenantAccess.RequireBusinessId()),
            cancellationToken);
        return Ok(ApiResponse<ExpenseSummaryDto>.SuccessResponse(ExpenseReadModelMapper.ToDto(summary)));
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
