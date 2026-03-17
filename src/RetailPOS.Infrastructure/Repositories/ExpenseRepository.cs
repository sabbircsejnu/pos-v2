using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Expense data access operations
/// </summary>
public class ExpenseRepository : IExpenseRepository
{
    private readonly RetailPOSDbContext _context;

    public ExpenseRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Expense?> GetByIdAsync(long id)
    {
        return await _context.Expenses
            .Include(e => e.Outlet)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<(IEnumerable<Expense>, int)> SearchAsync(
        string? category, long? outletId, DateTime? startDate, DateTime? endDate, int pageNumber, int pageSize)
    {
        var query = _context.Expenses
            .Include(e => e.Outlet)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(e => e.Category.ToLower().Contains(category.ToLower()));

        if (outletId.HasValue)
            query = query.Where(e => e.OutletId == outletId.Value);

        if (startDate.HasValue)
            query = query.Where(e => e.ExpenseDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(e => e.ExpenseDate <= endDate.Value);

        var totalCount = await query.CountAsync();

        var expenses = await query
            .OrderByDescending(e => e.ExpenseDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (expenses, totalCount);
    }

    public async Task<Expense> CreateAsync(Expense expense)
    {
        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(expense.Id))!;
    }

    public async Task<Expense> UpdateAsync(Expense expense)
    {
        _context.Expenses.Update(expense);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(expense.Id))!;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var expense = await _context.Expenses.FindAsync(id);
        if (expense == null) return false;

        _context.Expenses.Remove(expense);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<Expense>> GetCurrentMonthAsync(long? outletId = null)
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var query = _context.Expenses
            .Include(e => e.Outlet)
            .Where(e => e.ExpenseDate >= start && e.ExpenseDate < end)
            .AsQueryable();

        if (outletId.HasValue)
            query = query.Where(e => e.OutletId == outletId.Value);

        return await query.OrderByDescending(e => e.ExpenseDate).ToListAsync();
    }
}
