using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Expenses;

public sealed class ExpenseReadStore(
    RetailPOSDbContext dbContext,
    IExpenseLocationAuthorizer locationAuthorizer) : IExpenseReadStore
{
    public async Task<IReadOnlyList<ExpensePaymentAccountReadModel>> GetPaymentAccountsAsync(
        long businessId,
        string? paymentSource,
        long? outletId,
        long? warehouseId,
        CancellationToken cancellationToken)
    {
        var accounts = dbContext.ChartAccounts
            .AsNoTracking()
            .Where(account => account.BusinessId == businessId
                && account.IsActive
                && (account.AccountType == ChartAccountTypes.Asset || account.AccountType == ChartAccountTypes.Liability));

        if (string.IsNullOrWhiteSpace(paymentSource))
        {
            accounts = accounts.Where(account => account.IsExpensePaymentAccount);
        }
        else
        {
            var source = paymentSource.Trim().ToLowerInvariant();
            if (!ExpensePaymentSources.All.Contains(source))
                throw new InvalidOperationException("Payment source must be bank, petty_cash or pos_drawer.");

            var map = await ExpensePaymentAccountMapReader.LoadAsync(
                dbContext,
                businessId,
                warehouseId.HasValue ? null : outletId,
                cancellationToken);
            var allowed = ExpensePaymentAccountPolicy.AllowedAccountIds(source, map).ToList();

            // The POS drawer has exactly one account, chosen by configuration rather than by the funding-account flag.
            accounts = source == ExpensePaymentSources.PosDrawer
                ? accounts.Where(account => allowed.Contains(account.Id))
                : accounts.Where(account => account.IsExpensePaymentAccount && allowed.Contains(account.Id));
        }

        return await accounts
            .OrderBy(account => account.AccountCode)
            .Select(account => new ExpensePaymentAccountReadModel(
                account.Id,
                account.AccountCode,
                account.AccountName,
                account.AccountType))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExpenseAccountOptionReadModel>> GetEligibleExpenseAccountsAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.ChartAccounts
            .AsNoTracking()
            .Where(account => account.BusinessId == businessId
                && account.IsActive
                && account.AccountType == ChartAccountTypes.Expense)
            .OrderBy(account => account.AccountCode)
            .Select(account => new
            {
                account.Id,
                Facts = new ExpenseAccountFacts(
                    account.AccountCode,
                    account.AccountType,
                    account.IsActive,
                    account.IsSystemAccount,
                    account.IsControlAccount,
                    account.AllowManualPosting,
                    account.ChildAccounts.Any(child => child.IsActive)),
                account.AccountName
            })
            .ToListAsync(cancellationToken);

        return candidates
            .Where(candidate => ExpenseAccountEligibilityPolicy.IsEligible(candidate.Facts))
            .Select(candidate => new ExpenseAccountOptionReadModel(candidate.Id, candidate.Facts.AccountCode, candidate.AccountName))
            .ToList();
    }

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
        // A narrowing filter is authorized explicitly so an out-of-scope location is refused rather than empty.
        if (filter.OutletId.HasValue || filter.WarehouseId.HasValue)
            await locationAuthorizer.AuthorizeAsync(filter.OutletId, filter.WarehouseId, cancellationToken);

        var query = (await ApplyLocationScopeAsync(
                dbContext.Expenses.AsNoTracking(),
                cancellationToken))
            .Include(expense => expense.Outlet)
            .Include(expense => expense.Warehouse)
            .Include(expense => expense.ExpenseCategory)
            .Where(expense => expense.BusinessId == filter.BusinessId);

        if (filter.WarehouseId.HasValue)
        {
            query = query.Where(expense => expense.WarehouseId == filter.WarehouseId.Value);
        }

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
            .Include(expense => expense.Warehouse)
            .Include(expense => expense.ExpenseCategory)
            .Where(expense => expense.BusinessId == businessId && expense.Id == expenseId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {expenseId} not found");
        await locationAuthorizer.AuthorizeAsync(expense.OutletId, expense.WarehouseId, cancellationToken);
        return ToReadModel(expense);
    }

    public async Task<ExpenseSummaryReadModel> GetCurrentMonthSummaryAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var expenses = await (await ApplyLocationScopeAsync(dbContext.Expenses.AsNoTracking(), cancellationToken))
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

    private async Task<IQueryable<Expense>> ApplyLocationScopeAsync(
        IQueryable<Expense> query,
        CancellationToken cancellationToken)
    {
        var scope = await locationAuthorizer.GetScopeAsync(cancellationToken);
        if (scope.IsBusinessWide)
            return query;

        var outletIds = scope.OutletIds.ToList();
        var warehouseIds = scope.WarehouseIds.ToList();
        return query.Where(expense =>
            (expense.OutletId != null && outletIds.Contains(expense.OutletId.Value))
            || (expense.WarehouseId != null && warehouseIds.Contains(expense.WarehouseId.Value)));
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
            expense.ApprovedBy,
            expense.WarehouseId,
            expense.Warehouse == null ? null : expense.Warehouse.Name);
}
