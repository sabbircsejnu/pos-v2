using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetailPOS.API.DTOs.Expense;
using RetailPOS.API.Models;
using RetailPOS.API.Services;

namespace RetailPOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "expenses.view")]
public class ExpensesController : ControllerBase
{
    private readonly IExpenseService _expenseService;
    private readonly ILogger<ExpensesController> _logger;

    public ExpensesController(IExpenseService expenseService, ILogger<ExpensesController> logger)
    {
        _expenseService = expenseService;
        _logger = logger;
    }

    /// <summary>List expenses with optional filters</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<ExpenseListDto>>> GetAll(
        [FromQuery] string? category,
        [FromQuery] long? outletId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var searchDto = new ExpenseSearchDto
        {
            Category = category,
            OutletId = outletId,
            StartDate = startDate,
            EndDate = endDate
        };
        var result = await _expenseService.SearchAsync(searchDto);
        return Ok(ApiResponse<ExpenseListDto>.SuccessResponse(result));
    }

    /// <summary>Paginated expense search</summary>
    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<ExpenseListDto>>> Search([FromBody] ExpenseSearchDto searchDto)
    {
        var result = await _expenseService.SearchAsync(searchDto);
        return Ok(ApiResponse<ExpenseListDto>.SuccessResponse(result));
    }

    /// <summary>Get expense by ID</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> GetById(long id)
    {
        var expense = await _expenseService.GetByIdAsync(id);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(expense));
    }

    /// <summary>Create a new expense</summary>
    [HttpPost]
    [Authorize(Policy = "expenses.create")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Create([FromBody] CreateExpenseDto dto)
    {
        var expense = await _expenseService.CreateAsync(dto);
        return CreatedAtAction(
            nameof(GetById),
            new { id = expense.Id },
            ApiResponse<ExpenseDto>.SuccessResponse(expense, "Expense created successfully"));
    }

    /// <summary>Update an expense</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "expenses.edit")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Update(long id, [FromBody] UpdateExpenseDto dto)
    {
        var expense = await _expenseService.UpdateAsync(id, dto);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(expense, "Expense updated successfully"));
    }

    /// <summary>Delete an expense</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "expenses.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(long id)
    {
        await _expenseService.DeleteAsync(id);
        return Ok(ApiResponse.SuccessResponse("Expense deleted successfully"));
    }

    /// <summary>Get expense summary grouped by category for the current month</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<ExpenseSummaryDto>>> GetSummary()
    {
        var summary = await _expenseService.GetCurrentMonthSummaryAsync();
        return Ok(ApiResponse<ExpenseSummaryDto>.SuccessResponse(summary));
    }
}
