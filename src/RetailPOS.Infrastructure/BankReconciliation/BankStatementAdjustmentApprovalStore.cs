using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankReconciliation;

public sealed class BankStatementAdjustmentApprovalStore(
    RetailPOSDbContext dbContext,
    IBankStatementAdjustmentJournalPoster journalPoster)
    : IBankStatementAdjustmentApprovalStore
{
    public async Task<StatementAdjustmentReadModel> ApproveAsync(
        long businessId,
        long actorUserId,
        long adjustmentId,
        DateTime postingDate,
        CancellationToken cancellationToken)
    {
        var adjustment = await dbContext.BankStatementAdjustments
            .Include(row => row.BankAccount)
            .SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == adjustmentId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Statement adjustment not found.");
        if (adjustment.Status == BankStatementAdjustmentStatuses.Approved)
            return Map(adjustment);
        if (adjustment.Status != BankStatementAdjustmentStatuses.Proposed)
            throw new InvalidOperationException(
                "Only proposed adjustments can be approved.");

        var postingAccountId = await journalPoster.ResolvePostingAccountIdAsync(
            adjustment.AdjustmentType,
            cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            adjustment.Status = BankStatementAdjustmentStatuses.Approved;
            adjustment.ApprovedBy = actorUserId;
            adjustment.ApprovedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            var journalEntryId = await journalPoster.CreateAndPostAsync(
                new BankStatementAdjustmentJournalRequest(
                    businessId,
                    adjustment.Id,
                    adjustment.AdjustmentType,
                    adjustment.Amount,
                    postingDate.Date,
                    adjustment.Reason,
                    adjustment.IdempotencyKey,
                    postingAccountId,
                    adjustment.BankAccount.ChartAccountId),
                actorUserId,
                cancellationToken);
            adjustment.JournalEntryId = journalEntryId;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Map(adjustment);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static StatementAdjustmentReadModel Map(BankStatementAdjustment row) =>
        new(
            row.Id,
            row.BankStatementLineId,
            row.AdjustmentType,
            row.Amount,
            row.Status,
            row.JournalEntryId,
            row.ReversalJournalEntryId);
}
