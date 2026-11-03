using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Expenses.Commands;

public sealed record DrawerExpenseDisbursementResult(
    long ExpenseId,
    long CashSessionId,
    long? MovementId,
    decimal Amount,
    bool Replayed);

/// <summary>
/// FR-EXP-APP-01: approval is an accounting decision, not proof that cash left the drawer. Only this explicit step
/// records the physical outflow, on the session that actually paid it, exactly once.
/// </summary>
public sealed record PayDrawerExpenseCommand(
    long BusinessId,
    long ActorUserId,
    long ExpenseId,
    long TerminalId) : ICommand<DrawerExpenseDisbursementResult>;

public sealed class PayDrawerExpenseCommandHandler(
    IExpenseDrawerStore expenses,
    ICashSessionStore sessions,
    ICashSessionMovementRecorder movements,
    ICashSessionEventWriter eventWriter,
    ICashSessionAuditWriter auditWriter,
    ICashSessionWriteOutletAuthorizer outletAuthorizer,
    TimeProvider clock) : IRequestHandler<PayDrawerExpenseCommand, DrawerExpenseDisbursementResult>
{
    public async Task<DrawerExpenseDisbursementResult> Handle(PayDrawerExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await expenses.FindAsync(request.BusinessId, request.ExpenseId, cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {request.ExpenseId} not found");
        if (!string.Equals(expense.PaymentSource, ExpensePaymentSources.PosDrawer, StringComparison.Ordinal))
            throw new CashSessionException(
                CashSessionErrorCodes.ExpenseNotPayable,
                "Only a POS Drawer expense is paid from a cash session.");
        if (!string.Equals(expense.Status, ExpenseStatuses.Approved, StringComparison.OrdinalIgnoreCase))
            throw new CashSessionException(
                CashSessionErrorCodes.ExpenseNotPayable,
                "The expense must be approved before cash can be paid out.");
        var outletId = expense.OutletId
            ?? throw new CashSessionException(CashSessionErrorCodes.ExpenseNotPayable, "A POS Drawer expense needs an outlet.");
        await outletAuthorizer.AuthorizeWriteAsync(outletId, cancellationToken);

        // A retry of a payment that already committed returns the original result, whatever the sessions look like now.
        if (expense.DisbursedCashSessionId.HasValue)
            return new DrawerExpenseDisbursementResult(expense.Id, expense.DisbursedCashSessionId.Value, null, expense.Amount, true);

        var amount = expense.Amount;
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await sessions.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var sessionId = await sessions.FindOpenSessionIdAsync(outletId, request.TerminalId, cancellationToken)
                ?? throw new CashSessionException(
                    CashSessionErrorCodes.NoActiveSession,
                    "A POS Drawer expense is paid from the open cash session of the paying terminal. Open one first.",
                    409);

            // The recorder takes the shared session lock; a closed or overdue session is refused there.
            var movement = await movements.RecordAsync(
                new CashSessionMovementRequest(
                    sessionId,
                    CashSessionMovementTypes.DrawerExpense,
                    -amount,
                    "expense",
                    expense.Id,
                    $"expense:{expense.Id}:drawer-disbursement",
                    null,
                    null,
                    expense.ExpenseNumber,
                    request.ActorUserId),
                cancellationToken);

            var replayed = !movement.Created;
            if (movement.Created)
            {
                if (!await expenses.MarkDisbursedAsync(expense.Id, sessionId, request.ActorUserId, now, cancellationToken))
                    throw new CashSessionException(
                        CashSessionErrorCodes.ExpenseAlreadyDisbursed,
                        "This expense was already paid out.",
                        409);

                await eventWriter.AppendAsync(
                    new CashSessionEvent(
                        CashSessionEventTypes.DrawerExpensePaid,
                        request.BusinessId,
                        "Expense",
                        expense.Id.ToString(),
                        "drawer-paid",
                        now,
                        new
                        {
                            businessId = request.BusinessId,
                            expenseId = expense.Id,
                            expenseNumber = expense.ExpenseNumber,
                            cashSessionId = sessionId,
                            movementId = movement.Movement.Id,
                            amount,
                            paidByUserId = request.ActorUserId
                        }),
                    cancellationToken);
                await expenses.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            if (!replayed)
            {
                await auditWriter.RecordAsync(
                    "DrawerExpensePaid",
                    $"Expense {expense.ExpenseNumber ?? expense.Id.ToString()} paid from cash session {sessionId}",
                    sessionId,
                    new Dictionary<string, object?>
                    {
                        ["expenseId"] = expense.Id,
                        ["amount"] = amount,
                        ["movementId"] = movement.Movement.Id,
                        ["paidByUserId"] = request.ActorUserId,
                        ["approvedByUserId"] = expense.ApprovedBy
                    },
                    cancellationToken);
            }

            return new DrawerExpenseDisbursementResult(expense.Id, movement.Movement.CashSessionId, movement.Movement.Id, amount, replayed);
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

/// <summary>
/// Reverses a drawer disbursement (e.g. the supplier returned the cash). It is a new inflow on the session that
/// receives the cash — never an edit of the session that paid — and is recorded once per expense.
/// </summary>
public sealed record ReverseDrawerExpenseCommand(
    long BusinessId,
    long ActorUserId,
    long ExpenseId,
    long TerminalId,
    string Reason) : ICommand<DrawerExpenseDisbursementResult>;

public sealed class ReverseDrawerExpenseCommandHandler(
    IExpenseDrawerStore expenses,
    ICashSessionStore sessions,
    ICashSessionMovementRecorder movements,
    ICashSessionEventWriter eventWriter,
    ICashSessionAuditWriter auditWriter,
    ICashSessionWriteOutletAuthorizer outletAuthorizer,
    TimeProvider clock) : IRequestHandler<ReverseDrawerExpenseCommand, DrawerExpenseDisbursementResult>
{
    public async Task<DrawerExpenseDisbursementResult> Handle(ReverseDrawerExpenseCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new CashSessionException(CashSessionErrorCodes.ExpenseNotPayable, "A reason is required to reverse a drawer expense.");

        var expense = await expenses.FindAsync(request.BusinessId, request.ExpenseId, cancellationToken)
            ?? throw new KeyNotFoundException($"Expense with ID {request.ExpenseId} not found");
        if (expense.DisbursedAtUtc is null || expense.DisbursedCashSessionId is null)
            throw new CashSessionException(CashSessionErrorCodes.ExpenseNotPayable, "This expense was never paid out of a drawer.");
        var outletId = expense.OutletId
            ?? throw new CashSessionException(CashSessionErrorCodes.ExpenseNotPayable, "A POS Drawer expense needs an outlet.");
        await outletAuthorizer.AuthorizeWriteAsync(outletId, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await sessions.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var sessionId = await sessions.FindOpenSessionIdAsync(outletId, request.TerminalId, cancellationToken)
                ?? throw new CashSessionException(
                    CashSessionErrorCodes.NoActiveSession,
                    "The returned cash goes into an open cash session. Open one first.",
                    409);
            var movement = await movements.RecordAsync(
                new CashSessionMovementRequest(
                    sessionId,
                    CashSessionMovementTypes.DrawerExpenseReversal,
                    expense.Amount,
                    "expense",
                    expense.Id,
                    $"expense:{expense.Id}:drawer-reversal",
                    null,
                    expense.DisbursedCashSessionId,
                    request.Reason.Trim(),
                    request.ActorUserId),
                cancellationToken);

            if (movement.Created)
            {
                await eventWriter.AppendAsync(
                    new CashSessionEvent(
                        CashSessionEventTypes.DrawerExpenseReversed,
                        request.BusinessId,
                        "Expense",
                        expense.Id.ToString(),
                        "drawer-reversed",
                        now,
                        new
                        {
                            businessId = request.BusinessId,
                            expenseId = expense.Id,
                            originalCashSessionId = expense.DisbursedCashSessionId,
                            cashSessionId = sessionId,
                            movementId = movement.Movement.Id,
                            amount = expense.Amount,
                            reason = request.Reason.Trim()
                        }),
                    cancellationToken);
                await expenses.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            if (movement.Created)
            {
                await auditWriter.RecordAsync(
                    "DrawerExpenseReversed",
                    $"Expense {expense.ExpenseNumber ?? expense.Id.ToString()} drawer payment reversed into session {sessionId}",
                    sessionId,
                    new Dictionary<string, object?>
                    {
                        ["expenseId"] = expense.Id,
                        ["originalCashSessionId"] = expense.DisbursedCashSessionId,
                        ["reason"] = request.Reason.Trim()
                    },
                    cancellationToken);
            }

            return new DrawerExpenseDisbursementResult(
                expense.Id, movement.Movement.CashSessionId, movement.Movement.Id, expense.Amount, !movement.Created);
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
