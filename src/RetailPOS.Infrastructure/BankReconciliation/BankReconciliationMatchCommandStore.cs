using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankReconciliation;

public sealed class BankReconciliationMatchCommandStore(
    RetailPOSDbContext dbContext) : IBankReconciliationMatchCommandStore
{
    public async Task<ReconciliationMatchReadModel> UnmatchAsync(
        long businessId,
        long actorUserId,
        UnmatchBankReconciliationInput input,
        CancellationToken cancellationToken)
    {
        var match = await dbContext.BankReconciliationMatches
            .Include(row => row.Lines)
            .SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == input.MatchId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Reconciliation match not found.");
        var session = await dbContext.BankReconciliationSessions
            .SingleAsync(
                row => row.Id == match.BankReconciliationSessionId,
                cancellationToken);
        if (session.Status == BankReconciliationSessionStatuses.Closed)
            throw new InvalidOperationException(
                "Closed reconciliation sessions cannot be unmatched.");
        if (match.Status == BankReconciliationMatchStatuses.Unmatched)
            return ToReadModel(match);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            match.Status = BankReconciliationMatchStatuses.Unmatched;
            match.UnmatchedAt = DateTime.UtcNow;
            match.UnmatchedBy = actorUserId;
            match.UnmatchReason = input.Reason.Trim();
            foreach (var line in match.Lines)
                line.IsActive = false;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToReadModel(match);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ReconciliationMatchReadModel> CreateAsync(
        long businessId,
        long actorUserId,
        CreateBankReconciliationMatchInput input,
        CancellationToken cancellationToken)
    {
        if (input.StatementLineIds.Count == 0 || input.JournalEntryLineIds.Count == 0)
            throw new InvalidOperationException(
                "A match requires at least one statement line and one GL line.");

        var session = await dbContext.BankReconciliationSessions
            .SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == input.SessionId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Reconciliation session not found.");
        if (session.Status == BankReconciliationSessionStatuses.Closed)
            throw new InvalidOperationException(
                "Closed reconciliation sessions cannot be matched.");

        var statementIds = input.StatementLineIds.Distinct().ToList();
        var glIds = input.JournalEntryLineIds.Distinct().ToList();
        var statements = await dbContext.BankStatementLines
            .Where(line =>
                statementIds.Contains(line.Id)
                && line.BusinessId == session.BusinessId
                && line.BankAccountId == session.BankAccountId
                && line.TransactionDate >= session.PeriodStart
                && line.TransactionDate < session.PeriodEnd.AddDays(1))
            .ToListAsync(cancellationToken);
        var accountId = await dbContext.BankAccounts
            .Where(account => account.Id == session.BankAccountId)
            .Select(account => account.ChartAccountId)
            .SingleAsync(cancellationToken);
        var glLines = await dbContext.JournalEntryLines
            .Include(line => line.JournalEntry)
            .Where(line =>
                glIds.Contains(line.Id)
                && line.JournalEntry.BusinessId == session.BusinessId
                && line.AccountId == accountId
                && line.JournalEntry.Status == JournalEntryStatuses.Posted
                && line.JournalEntry.PostingDate >= session.PeriodStart
                && line.JournalEntry.PostingDate < session.PeriodEnd.AddDays(1))
            .ToListAsync(cancellationToken);
        if (statements.Count != statementIds.Count || glLines.Count != glIds.Count)
            throw new InvalidOperationException(
                "One or more candidates are outside the reconciliation scope.");
        if (await dbContext.BankReconciliationMatchLines.AnyAsync(
                line =>
                    line.IsActive
                    && ((line.BankStatementLineId.HasValue
                            && statementIds.Contains(line.BankStatementLineId.Value))
                        || (line.JournalEntryLineId.HasValue
                            && glIds.Contains(line.JournalEntryLineId.Value))),
                cancellationToken))
            throw new InvalidOperationException(
                "One or more candidates are already matched.");

        var statementTotal = statements.Sum(line => line.SignedAmount);
        var glTotal = glLines.Sum(line => line.DebitAmount - line.CreditAmount);
        if (statementTotal != glTotal)
            throw new InvalidOperationException(
                "Match totals must be exactly equal; tolerance is 0.00.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var match = new BankReconciliationMatch
            {
                BusinessId = session.BusinessId,
                BankAccountId = session.BankAccountId,
                BankReconciliationSessionId = session.Id,
                StatementTotal = statementTotal,
                GlTotal = glTotal,
                CreatedBy = actorUserId,
                CreatedAt = DateTime.UtcNow,
                Status = BankReconciliationMatchStatuses.Effective
            };
            dbContext.BankReconciliationMatches.Add(match);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var line in statements)
                dbContext.BankReconciliationMatchLines.Add(
                    new BankReconciliationMatchLine
                    {
                        BankReconciliationMatchId = match.Id,
                        BankStatementLineId = line.Id,
                        SignedAmount = line.SignedAmount,
                        IsActive = true
                    });
            foreach (var line in glLines)
                dbContext.BankReconciliationMatchLines.Add(
                    new BankReconciliationMatchLine
                    {
                        BankReconciliationMatchId = match.Id,
                        JournalEntryLineId = line.Id,
                        SignedAmount = line.DebitAmount - line.CreditAmount,
                        IsActive = true
                    });

            session.Status = BankReconciliationSessionStatuses.InProgress;
            session.UpdatedAt = DateTime.UtcNow;
            session.UpdatedBy = actorUserId;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ReconciliationMatchReadModel(
                match.Id,
                match.BankReconciliationSessionId,
                match.Status,
                match.StatementTotal,
                match.GlTotal,
                statementIds,
                glIds);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static ReconciliationMatchReadModel ToReadModel(
        BankReconciliationMatch match) =>
        new(
            match.Id,
            match.BankReconciliationSessionId,
            match.Status,
            match.StatementTotal,
            match.GlTotal,
            match.Lines
                .Where(line => line.BankStatementLineId.HasValue)
                .Select(line => line.BankStatementLineId!.Value)
                .ToList(),
            match.Lines
                .Where(line => line.JournalEntryLineId.HasValue)
                .Select(line => line.JournalEntryLineId!.Value)
                .ToList());
}
