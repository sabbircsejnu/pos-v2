using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.IntegrationTests.Infrastructure;

/// <summary>For harnesses without an integration outbox (SQLite/InMemory): events are accepted and dropped.</summary>
public sealed class NullCashSessionEvents : RetailPOS.Application.Pos.Abstractions.ICashSessionEventWriter
{
    public Task AppendAsync(RetailPOS.Application.Pos.Abstractions.CashSessionEvent cashSessionEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>For harnesses that predate cash refunds: the sale holds no drawer cash to pay back.</summary>
public sealed class NoCashRefunds : ISaleCashRefundRecorder
{
    public Task<SaleCashRefundResult?> RecordAsync(SaleCashRefundRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<SaleCashRefundResult?>(null);
}

/// <summary>
/// Mirrors <c>ExpenseMakerCheckerPolicy</c> without HTTP: a maker may approve their own drawer/petty-cash expense only
/// when the Business setting is on and the actor is flagged as an owner holding the explicit permission.
/// </summary>
public sealed class StubMakerCheckerPolicy(bool ownerExceptionEnabled = false, bool actorIsOwnerWithExplicitPermission = false)
    : IExpenseMakerCheckerPolicy
{
    public Task<bool> EvaluateAsync(Expense expense, long approverUserId, CancellationToken cancellationToken)
    {
        if (!ExpensePaymentSources.RequiresMakerChecker(expense.PaymentSource))
            return Task.FromResult(false);
        if (expense.CreatedBy != approverUserId && expense.SubmittedBy != approverUserId)
            return Task.FromResult(false);
        if (ownerExceptionEnabled && actorIsOwnerWithExplicitPermission)
            return Task.FromResult(true);

        throw new CashSessionException(CashSessionErrorCodes.ExpenseMakerChecker, "maker-checker", 403);
    }
}
