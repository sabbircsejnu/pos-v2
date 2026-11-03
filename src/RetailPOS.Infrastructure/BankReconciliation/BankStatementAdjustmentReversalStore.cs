using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankReconciliation;

public sealed class BankStatementAdjustmentReversalStore(
    RetailPOSDbContext dbContext,
    IBankStatementAdjustmentJournalPoster journalPoster)
    : IBankStatementAdjustmentReversalStore
{
    public async Task<StatementAdjustmentReadModel> ReverseAsync(
        long businessId,
        long actorUserId,
        long adjustmentId,
        string reason,
        DateTime postingDate,
        CancellationToken cancellationToken)
    {
        var adjustment = await dbContext.BankStatementAdjustments
            .SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == adjustmentId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Statement adjustment not found.");
        if (adjustment.Status == BankStatementAdjustmentStatuses.Reversed)
            return Map(adjustment);
        if (adjustment.Status != BankStatementAdjustmentStatuses.Approved
            || !adjustment.JournalEntryId.HasValue)
        {
            throw new InvalidOperationException(
                "Only approved adjustments can be reversed.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var reversalJournalEntryId = await journalPoster.ReverseAsync(
                businessId,
                adjustment.JournalEntryId.Value,
                reason.Trim(),
                postingDate.Date,
                actorUserId,
                cancellationToken);
            adjustment.ReversalJournalEntryId = reversalJournalEntryId;
            adjustment.Status = BankStatementAdjustmentStatuses.Reversed;
            adjustment.ReversedAt = DateTime.UtcNow;
            adjustment.ReversedBy = actorUserId;
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
