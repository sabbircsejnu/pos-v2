using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankReconciliation;

public sealed class BankStatementAdjustmentCommandStore(RetailPOSDbContext dbContext)
    : IBankStatementAdjustmentCommandStore
{
    public async Task<StatementAdjustmentReadModel> ProposeAsync(
        long businessId,
        long actorUserId,
        ProposeBankStatementAdjustmentInput input,
        CancellationToken cancellationToken)
    {
        var key = input.IdempotencyKey.Trim();
        var existing = await dbContext.BankStatementAdjustments
            .SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.IdempotencyKey == key,
                cancellationToken);
        if (existing is not null)
            return Map(existing);

        var line = await dbContext.BankStatementLines
            .Include(row => row.BankAccount)
            .SingleOrDefaultAsync(
                row => row.Id == input.BankStatementLineId
                    && row.BusinessId == businessId
                    && row.BankAccount.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Statement line not found in this business.");
        if (input.AdjustmentType is not (
                BankStatementAdjustmentTypes.BankFee
                or BankStatementAdjustmentTypes.BankInterest))
            throw new InvalidOperationException(
                "Unsupported statement adjustment type.");
        if (input.Amount > Math.Abs(line.SignedAmount))
            throw new InvalidOperationException(
                "Adjustment amount cannot exceed the linked statement movement.");

        var row = new BankStatementAdjustment
        {
            BusinessId = businessId,
            BankAccountId = line.BankAccountId,
            BankStatementLineId = line.Id,
            AdjustmentType = input.AdjustmentType,
            Amount = input.Amount,
            Reason = input.Reason.Trim(),
            IdempotencyKey = key,
            ProposedBy = actorUserId,
            ProposedAt = DateTime.UtcNow,
            CreatedBy = actorUserId,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.BankStatementAdjustments.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(row);
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
