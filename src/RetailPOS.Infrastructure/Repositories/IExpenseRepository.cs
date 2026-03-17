using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IExpenseRepository
{
    Task<Expense?> GetByIdAsync(long id);
    Task<(IEnumerable<Expense>, int)> SearchAsync(
        string? category, long? outletId, DateTime? startDate, DateTime? endDate, int pageNumber, int pageSize);
    Task<Expense> CreateAsync(Expense expense);
    Task<Expense> UpdateAsync(Expense expense);
    Task<bool> DeleteAsync(long id);
    Task<IEnumerable<Expense>> GetCurrentMonthAsync(long? outletId = null);
}
