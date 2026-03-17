using RetailPOS.API.DTOs.Expense;

namespace RetailPOS.API.Services;

public interface IExpenseService
{
    Task<ExpenseListDto> SearchAsync(ExpenseSearchDto searchDto);
    Task<ExpenseDto> GetByIdAsync(long id);
    Task<ExpenseDto> CreateAsync(CreateExpenseDto dto);
    Task<ExpenseDto> UpdateAsync(long id, UpdateExpenseDto dto);
    Task DeleteAsync(long id);
    Task<ExpenseSummaryDto> GetCurrentMonthSummaryAsync();
}
