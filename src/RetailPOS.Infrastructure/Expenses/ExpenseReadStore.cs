using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Expenses;

public sealed class ExpenseReadStore(RetailPOSDbContext dbContext) : IExpenseReadStore
{
    public async Task<IReadOnlyList<ExpenseCategoryReadModel>> GetCategoriesAsync(
        long businessId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ExpenseCategories
            .AsNoTracking()
            .Where(category => category.BusinessId == businessId);

        if (!includeInactive)
        {
            query = query.Where(category => category.IsActive);
        }

        return await query
            .OrderBy(category => category.Name)
            .Select(category => new ExpenseCategoryReadModel(
                category.Id,
                category.Name,
                category.ExpenseAccountId,
                category.ExpenseAccount.AccountCode,
                category.ExpenseAccount.AccountName,
                category.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<ExpenseListReadModel> SearchAsync(
        ExpenseSearchFilter filter,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Expenses
            .AsNoTracking()
            .Include(expense => expense.Outlet)
            .Include(expense => expense.ExpenseCategory)
            .Where(expense => expense.BusinessId == filter.BusinessId);

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            var category = filter.Category.Trim().ToLowerInvariant();
            query = query.Where(expense => expense.Category.ToLower().Contains(category));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.Trim();
            query = query.Where(expense => expense.Status == status);
        }

        if (filter.OutletId.HasValue)
        {
            query = query.Where(expense => expense.OutletId == filter.OutletId.Value);
        }

        if (filter.StartDate.HasValue)
        {
            query = query.Where(expense => expense.ExpenseDate >= filter.StartDate.Value.Date);
        }

        if (filter.EndDate.HasValue)
        {
            query = query.Where(expense => expense.ExpenseDate <= filter.EndDate.Value.Date);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var expenseRows = await query
            .OrderByDescending(expense => expense.ExpenseDate)
            .ThenByDescending(expense => expense.Id)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        var expenses = expenseRows.Select(ToReadModel).ToList();

        return new ExpenseListReadModel(
            expenses,
            totalCount,
            filter.PageNumber,
            filter.PageSize,
            expenses.Sum(expense => expense.Amount));
    }

    public async Task<ExpenseReadModel> GetByIdAsync(
        long businessId,
        long expenseId,
        CancellationToken cancellationToken)
    {
        var expense = await dbContext.Expenses
            .AsNoTracking()
            .Include(expense => expense.Outlet)
            .Include(expense => expense.ExpenseCategory)
            .Where(expense => expense.BusinessId == businessId && expense.Id == expenseId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {expenseId} not found");
        return ToReadModel(expense);
    }

    public async Task<ExpenseSummaryReadModel> GetCurrentMonthSummaryAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var expenses = await dbContext.Expenses
            .AsNoTracking()
            .Where(expense =>
                expense.BusinessId == businessId
                && expense.ExpenseDate >= start
                && expense.ExpenseDate < end
                && expense.Status != ExpenseStatuses.Cancelled)
            .Select(expense => new { expense.Category, expense.Amount })
            .ToListAsync(cancellationToken);

        return new ExpenseSummaryReadModel(
            expenses.Sum(expense => expense.Amount),
            expenses
                .GroupBy(expense => expense.Category)
                .ToDictionary(group => group.Key, group => group.Sum(expense => expense.Amount)),
            expenses.Count);
    }

    private static ExpenseReadModel ToReadModel(Expense expense) =>
        new(
            expense.Id,
            expense.ExpenseNumber,
            expense.Category,
            expense.ExpenseCategoryId,
            expense.ExpenseAccountId,
            expense.Amount,
            expense.PaymentMethodId,
            expense.PaymentAccountId,
            expense.ReferenceNumber,
            expense.Description,
            expense.AttachmentPath,
            expense.ExpenseDate,
            expense.OutletId,
            expense.Outlet == null ? null : expense.Outlet.Name,
            expense.Status,
            expense.JournalEntryId,
            expense.CreatedAt,
            expense.SubmittedAt,
            expense.ApprovedAt,
            expense.RejectedAt,
            expense.RejectionReason,
            expense.CancelledAt,
            expense.CancellationReason,
            expense.PaymentSource,
            expense.DisbursedAtUtc,
            expense.DisbursedCashSessionId,
            expense.SelfApprovedUnderOwnerException,
            expense.CreatedBy,
            expense.SubmittedBy,
            expense.ApprovedBy);
}
