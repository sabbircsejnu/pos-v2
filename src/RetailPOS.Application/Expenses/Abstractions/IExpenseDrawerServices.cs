using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Expenses.Abstractions;

/// <summary>
/// D7 maker-checker. Called by the approval step; returns true only when the Business Owner self-approval exception
/// was legitimately used (so it can be persisted as evidence) and throws <c>CashSessionException</c> when the approver
/// is the creator/submitter of a POS Drawer or Petty Cash expense without that exception.
/// </summary>
public interface IExpenseMakerCheckerPolicy
{
    Task<bool> EvaluateAsync(Expense expense, long approverUserId, CancellationToken cancellationToken);
}

public interface IExpenseDrawerStore
{
    Task<Expense?> FindAsync(long businessId, long expenseId, CancellationToken cancellationToken);

    /// <summary>Conditional <c>UPDATE … WHERE disbursed_at_utc IS NULL</c>; false when already disbursed.</summary>
    Task<bool> MarkDisbursedAsync(
        long expenseId,
        long cashSessionId,
        long actorUserId,
        DateTime disbursedAtUtc,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
