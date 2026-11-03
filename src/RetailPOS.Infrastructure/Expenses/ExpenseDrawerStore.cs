using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Expenses;

public sealed class ExpenseDrawerStore(RetailPOSDbContext dbContext) : IExpenseDrawerStore
{
    public Task<Expense?> FindAsync(long businessId, long expenseId, CancellationToken cancellationToken) =>
        dbContext.Expenses.AsNoTracking().FirstOrDefaultAsync(
            expense => expense.BusinessId == businessId && expense.Id == expenseId,
            cancellationToken);

    public async Task<bool> MarkDisbursedAsync(
        long expenseId,
        long cashSessionId,
        long actorUserId,
        DateTime disbursedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var tracked = await dbContext.Expenses.FirstOrDefaultAsync(
                expense => expense.Id == expenseId && expense.DisbursedAtUtc == null, cancellationToken);
            if (tracked is null)
                return false;
            tracked.DisbursedAtUtc = disbursedAtUtc;
            tracked.DisbursedByUserId = actorUserId;
            tracked.DisbursedCashSessionId = cashSessionId;
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        return await dbContext.Expenses
            .Where(expense => expense.Id == expenseId && expense.DisbursedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(expense => expense.DisbursedAtUtc, disbursedAtUtc)
                    .SetProperty(expense => expense.DisbursedByUserId, actorUserId)
                    .SetProperty(expense => expense.DisbursedCashSessionId, cashSessionId),
                cancellationToken) == 1;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
