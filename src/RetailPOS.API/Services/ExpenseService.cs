using RetailPOS.API.DTOs.Expense;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.API.Services;

/// <summary>
/// Service implementation for Expense business logic
/// </summary>
public class ExpenseService : IExpenseService
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly ILogger<ExpenseService> _logger;

    public ExpenseService(IExpenseRepository expenseRepository, ILogger<ExpenseService> logger)
    {
        _expenseRepository = expenseRepository;
        _logger = logger;
    }

    /// <summary>Searches expenses with filters and pagination</summary>
    public async Task<ExpenseListDto> SearchAsync(ExpenseSearchDto searchDto)
    {
        var (expenses, totalCount) = await _expenseRepository.SearchAsync(
            searchDto.Category,
            searchDto.OutletId,
            searchDto.StartDate,
            searchDto.EndDate,
            searchDto.PageNumber,
            searchDto.PageSize);

        var list = expenses.ToList();
        return new ExpenseListDto
        {
            Expenses = list.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            PageNumber = searchDto.PageNumber,
            PageSize = searchDto.PageSize,
            TotalAmount = list.Sum(e => e.Amount)
        };
    }

    /// <summary>Gets an expense by ID</summary>
    public async Task<ExpenseDto> GetByIdAsync(long id)
    {
        var expense = await _expenseRepository.GetByIdAsync(id);
        if (expense == null)
            throw new KeyNotFoundException($"Expense with ID {id} not found");

        return MapToDto(expense);
    }

    /// <summary>Creates a new expense record</summary>
    public async Task<ExpenseDto> CreateAsync(CreateExpenseDto dto)
    {
        var expense = new Expense
        {
            Category = dto.Category.Trim(),
            Amount = dto.Amount,
            Description = dto.Description?.Trim(),
            ExpenseDate = dto.ExpenseDate,
            OutletId = dto.OutletId
        };

        var created = await _expenseRepository.CreateAsync(expense);
        _logger.LogInformation("Expense {ExpenseId} created", created.Id);
        return MapToDto(created);
    }

    /// <summary>Updates an existing expense record</summary>
    public async Task<ExpenseDto> UpdateAsync(long id, UpdateExpenseDto dto)
    {
        var expense = await _expenseRepository.GetByIdAsync(id);
        if (expense == null)
            throw new KeyNotFoundException($"Expense with ID {id} not found");

        expense.Category = dto.Category.Trim();
        expense.Amount = dto.Amount;
        expense.Description = dto.Description?.Trim();
        expense.ExpenseDate = dto.ExpenseDate;
        expense.OutletId = dto.OutletId;

        var updated = await _expenseRepository.UpdateAsync(expense);
        _logger.LogInformation("Expense {ExpenseId} updated", id);
        return MapToDto(updated);
    }

    /// <summary>Deletes an expense record</summary>
    public async Task DeleteAsync(long id)
    {
        var expense = await _expenseRepository.GetByIdAsync(id);
        if (expense == null)
            throw new KeyNotFoundException($"Expense with ID {id} not found");

        await _expenseRepository.DeleteAsync(id);
        _logger.LogInformation("Expense {ExpenseId} deleted", id);
    }

    /// <summary>Returns expense summary grouped by category for the current month</summary>
    public async Task<ExpenseSummaryDto> GetCurrentMonthSummaryAsync()
    {
        var expenses = await _expenseRepository.GetCurrentMonthAsync();
        var list = expenses.ToList();

        return new ExpenseSummaryDto
        {
            TotalAmount = list.Sum(e => e.Amount),
            Count = list.Count,
            ByCategory = list
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount))
        };
    }

    private static ExpenseDto MapToDto(Expense e) => new ExpenseDto
    {
        Id = e.Id,
        Category = e.Category,
        Amount = e.Amount,
        Description = e.Description,
        ExpenseDate = e.ExpenseDate,
        OutletId = e.OutletId,
        OutletName = e.Outlet?.Name
    };
}
