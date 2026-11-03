using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankReconciliation;

public sealed class BankReconciliationCloseCommandStore(
    RetailPOSDbContext dbContext,
    IBankReconciliationReadStore readStore)
    : IBankReconciliationCloseCommandStore
{
    public async Task<BankReconciliationSessionReadModel> CloseAsync(
        long businessId,
        long actorUserId,
        long sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var session = await dbContext.BankReconciliationSessions
                .SingleOrDefaultAsync(
                    row => row.BusinessId == businessId && row.Id == sessionId,
                    cancellationToken)
                ?? throw new KeyNotFoundException("Reconciliation session not found.");
            if (session.Status == BankReconciliationSessionStatuses.Closed)
            {
                await transaction.CommitAsync(cancellationToken);
                return Map(session);
            }
            if (session.Status != BankReconciliationSessionStatuses.ReadyForClose)
                throw new InvalidOperationException(
                    "Session must be ready for close and Finance approval is required.");

            var calculation = await readStore.GetCalculationAsync(
                businessId,
                sessionId,
                cancellationToken);
            if (calculation.UnmatchedStatementCount > 0
                || calculation.UnmatchedGlCount > 0
                || calculation.UnresolvedExceptionCount > 0
                || calculation.ReconciliationDifference != 0m)
                throw new InvalidOperationException(
                    "Reconciliation readiness changed; close was rejected.");

            session.Status = BankReconciliationSessionStatuses.Closed;
            session.ClosedBy = actorUserId;
            session.ClosedAt = DateTime.UtcNow;
            session.ClosedDifference = calculation.ReconciliationDifference;
            session.CloseResultReference = reason.Trim();
            session.UpdatedAt = DateTime.UtcNow;
            session.UpdatedBy = actorUserId;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Map(session);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<BankReconciliationSessionReadModel> PrepareForCloseAsync(
        long businessId,
        long actorUserId,
        long sessionId,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.BankReconciliationSessions
            .SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == sessionId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Reconciliation session not found.");
        if (session.Status == BankReconciliationSessionStatuses.Closed)
            return Map(session);

        var calculation = await readStore.GetCalculationAsync(
            businessId,
            sessionId,
            cancellationToken);
        if (!session.StatementOpeningBalance.HasValue
            || !session.StatementClosingBalance.HasValue
            || calculation.UnmatchedStatementCount > 0
            || calculation.UnmatchedGlCount > 0
            || calculation.UnresolvedExceptionCount > 0
            || calculation.ReconciliationDifference != 0m)
            throw new InvalidOperationException(
                "Reconciliation is not ready for close: evidence, matches, exceptions, or difference remain unresolved.");

        var now = DateTime.UtcNow;
        session.Status = BankReconciliationSessionStatuses.ReadyForClose;
        session.ReconciledBy = actorUserId;
        session.ReconciledAt = now;
        session.UpdatedAt = now;
        session.UpdatedBy = actorUserId;
        session.UpdatedBy = actorUserId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(session);
    }

    public async Task<BankReconciliationSessionReadModel> ReopenAsync(
        long businessId,
        long actorUserId,
        long sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.BankReconciliationSessions
            .SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == sessionId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Reconciliation session not found.");
        if (session.Status != BankReconciliationSessionStatuses.Closed)
            return Map(session);

        session.Status = BankReconciliationSessionStatuses.InProgress;
        session.ReopenedAt = DateTime.UtcNow;
        session.ReopenedBy = actorUserId;
        session.ReopenReason = reason.Trim();
        session.UpdatedAt = DateTime.UtcNow;
        session.UpdatedBy = actorUserId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(session);
    }

    private static BankReconciliationSessionReadModel Map(
        BankReconciliationSession session) =>
        new(
            session.Id,
            session.BusinessId,
            session.BankAccountId,
            session.PeriodStart,
            session.PeriodEnd,
            session.StatementOpeningBalance,
            session.StatementClosingBalance,
            session.GlOpeningBalance,
            session.Status);
}
