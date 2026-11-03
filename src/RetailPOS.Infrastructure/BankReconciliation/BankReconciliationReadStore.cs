using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankReconciliation;

public sealed class BankReconciliationReadStore(RetailPOSDbContext dbContext)
    : IBankReconciliationReadStore
{
    public async Task<IReadOnlyList<BankReconciliationSessionReadModel>> ListSessionsAsync(
        long businessId,
        long? bankAccountId,
        CancellationToken cancellationToken) =>
        await dbContext.BankReconciliationSessions
            .AsNoTracking()
            .Where(session =>
                session.BusinessId == businessId
                && (!bankAccountId.HasValue
                    || session.BankAccountId == bankAccountId.Value))
            .OrderByDescending(session => session.PeriodEnd)
            .Select(session => new BankReconciliationSessionReadModel(
                session.Id,
                session.BusinessId,
                session.BankAccountId,
                session.PeriodStart,
                session.PeriodEnd,
                session.StatementOpeningBalance,
                session.StatementClosingBalance,
                session.GlOpeningBalance,
                session.Status))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReconciliationCandidateReadModel>> FindStatementCandidatesAsync(
        long businessId,
        long sessionId,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.BankReconciliationSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == sessionId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Reconciliation session not found.");
        var matchedLineIds = dbContext.BankReconciliationMatchLines
            .Where(line => line.IsActive && line.BankStatementLineId.HasValue)
            .Select(line => line.BankStatementLineId!.Value);
        return await dbContext.BankStatementLines
            .AsNoTracking()
            .Where(line =>
                line.BusinessId == session.BusinessId
                && line.BankAccountId == session.BankAccountId
                && line.TransactionDate >= session.PeriodStart
                && line.TransactionDate < session.PeriodEnd.AddDays(1)
                && !matchedLineIds.Contains(line.Id))
            .OrderBy(line => line.TransactionDate)
            .Select(line => new ReconciliationCandidateReadModel(
                line.Id,
                null,
                line.TransactionDate,
                line.SignedAmount,
                line.Description,
                line.ExternalTransactionReference,
                null,
                null,
                null,
                null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReconciliationCandidateReadModel>> FindGlCandidatesAsync(
        long businessId,
        long sessionId,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.BankReconciliationSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == sessionId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Reconciliation session not found.");
        var accountId = await dbContext.BankAccounts
            .Where(account => account.Id == session.BankAccountId)
            .Select(account => account.ChartAccountId)
            .SingleAsync(cancellationToken);
        var matchedLineIds = dbContext.BankReconciliationMatchLines
            .Where(line => line.IsActive && line.JournalEntryLineId.HasValue)
            .Select(line => line.JournalEntryLineId!.Value);
        return await dbContext.JournalEntryLines
            .AsNoTracking()
            .Where(line =>
                line.JournalEntry.BusinessId == session.BusinessId
                && line.AccountId == accountId
                && line.JournalEntry.Status == JournalEntryStatuses.Posted
                && line.JournalEntry.PostingDate >= session.PeriodStart
                && line.JournalEntry.PostingDate < session.PeriodEnd.AddDays(1)
                && !matchedLineIds.Contains(line.Id))
            .OrderBy(line => line.JournalEntry.PostingDate)
            .Select(line => new ReconciliationCandidateReadModel(
                null,
                line.Id,
                line.JournalEntry.PostingDate,
                line.DebitAmount - line.CreditAmount,
                line.Description ?? "",
                null,
                line.JournalEntryId,
                line.JournalEntry.JournalNumber,
                line.JournalEntry.ReferenceType,
                line.JournalEntry.ReferenceId))
            .ToListAsync(cancellationToken);
    }

    public async Task<BankReconciliationCalculationReadModel> GetCalculationAsync(
        long businessId,
        long sessionId,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.BankReconciliationSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.BusinessId == businessId && row.Id == sessionId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Reconciliation session not found.");
        var statements = await dbContext.BankStatementLines
            .AsNoTracking()
            .Where(line =>
                line.BusinessId == session.BusinessId
                && line.BankAccountId == session.BankAccountId
                && line.TransactionDate >= session.PeriodStart
                && line.TransactionDate < session.PeriodEnd.AddDays(1))
            .Select(line => line.SignedAmount)
            .ToListAsync(cancellationToken);
        var accountId = await dbContext.BankAccounts
            .Where(account => account.Id == session.BankAccountId)
            .Select(account => account.ChartAccountId)
            .SingleAsync(cancellationToken);
        var glLines = await dbContext.JournalEntryLines
            .AsNoTracking()
            .Where(line =>
                line.JournalEntry.BusinessId == session.BusinessId
                && line.AccountId == accountId
                && line.JournalEntry.Status == JournalEntryStatuses.Posted
                && line.JournalEntry.PostingDate >= session.PeriodStart
                && line.JournalEntry.PostingDate < session.PeriodEnd.AddDays(1))
            .Select(line => new
            {
                line.DebitAmount,
                line.CreditAmount
            })
            .ToListAsync(cancellationToken);
        var activeMatches = await dbContext.BankReconciliationMatches
            .AsNoTracking()
            .Where(match =>
                match.BankReconciliationSessionId == session.Id
                && match.Status == BankReconciliationMatchStatuses.Effective)
            .Select(match => new
            {
                match.StatementTotal,
                match.GlTotal,
                StatementLineIds = match.Lines
                    .Where(line => line.IsActive && line.BankStatementLineId.HasValue)
                    .Select(line => line.BankStatementLineId!.Value)
                    .ToList(),
                JournalEntryLineIds = match.Lines
                    .Where(line => line.IsActive && line.JournalEntryLineId.HasValue)
                    .Select(line => line.JournalEntryLineId!.Value)
                    .ToList()
            })
            .ToListAsync(cancellationToken);
        var matchedStatementIds = activeMatches
            .SelectMany(match => match.StatementLineIds)
            .ToHashSet();
        var matchedGlIds = activeMatches
            .SelectMany(match => match.JournalEntryLineIds)
            .ToHashSet();
        var matchedStatement = activeMatches.Sum(match => match.StatementTotal);
        var matchedGl = activeMatches.Sum(match => match.GlTotal);
        var statementMovement = statements.Sum();
        var glMovement = glLines.Sum(line => line.DebitAmount - line.CreditAmount);
        var opening = session.StatementOpeningBalance ?? 0m;
        var glOpening = session.GlOpeningBalance ?? 0m;
        var statementClosing = session.StatementClosingBalance ?? opening + statementMovement;
        var glClosing = glOpening + glMovement;
        var unresolvedExceptions = await dbContext.BankStatementExceptions
            .CountAsync(
                exception =>
                    exception.BusinessId == session.BusinessId
                    && exception.BankAccountId == session.BankAccountId
                    && exception.Status == BankStatementExceptionStatuses.Open
                    && exception.StatementLine.TransactionDate >= session.PeriodStart
                    && exception.StatementLine.TransactionDate < session.PeriodEnd.AddDays(1),
                cancellationToken);

        return new BankReconciliationCalculationReadModel(
            opening,
            statementMovement,
            statementClosing,
            glOpening,
            glMovement,
            glClosing,
            matchedStatement,
            matchedStatementIds.Count,
            matchedGl,
            matchedGlIds.Count,
            statementMovement - matchedStatement,
            statements.Count - matchedStatementIds.Count,
            glMovement - matchedGl,
            glLines.Count - matchedGlIds.Count,
            statementClosing - glClosing,
            unresolvedExceptions);
    }

    public async Task<IReadOnlyList<BankAccountReadModel>> FindActiveBankAccountsAsync(
        long businessId,
        CancellationToken cancellationToken) =>
        await dbContext.BankAccounts
            .AsNoTracking()
            .Where(account => account.BusinessId == businessId && account.IsActive)
            .OrderBy(account => account.DisplayName)
            .Select(account => new BankAccountReadModel(
                account.Id,
                account.DisplayName,
                account.ChartAccountId,
                account.OutletId,
                account.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReconciliationMatchReadModel>> FindMatchesAsync(
        long sessionId,
        long businessId,
        CancellationToken cancellationToken) =>
        await dbContext.BankReconciliationMatches
            .AsNoTracking()
            .Where(match =>
                match.BusinessId == businessId
                && match.BankReconciliationSessionId == sessionId)
            .Select(match => new ReconciliationMatchReadModel(
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
                    .ToList()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StatementAdjustmentReadModel>> FindAdjustmentsAsync(
        long bankAccountId,
        long businessId,
        CancellationToken cancellationToken) =>
        await dbContext.BankStatementAdjustments
            .AsNoTracking()
            .Where(adjustment =>
                adjustment.BusinessId == businessId
                && adjustment.BankAccountId == bankAccountId)
            .OrderByDescending(adjustment => adjustment.Id)
            .Select(adjustment => new StatementAdjustmentReadModel(
                adjustment.Id,
                adjustment.BankStatementLineId,
                adjustment.AdjustmentType,
                adjustment.Amount,
                adjustment.Status,
                adjustment.JournalEntryId,
                adjustment.ReversalJournalEntryId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StatementExceptionReadModel>> FindExceptionsAsync(
        long bankAccountId,
        long businessId,
        CancellationToken cancellationToken) =>
        await dbContext.BankStatementExceptions
            .AsNoTracking()
            .Where(exception =>
                exception.BusinessId == businessId
                && exception.BankAccountId == bankAccountId)
            .OrderByDescending(exception => exception.Id)
            .Select(exception => new StatementExceptionReadModel(
                exception.Id,
                exception.BankStatementLineId,
                exception.Classification,
                exception.Status,
                exception.Reason,
                exception.ResolutionNotes,
                exception.CreatedBy,
                exception.CreatedAt,
                exception.ResolvedBy,
                exception.ResolvedAt))
            .ToListAsync(cancellationToken);
}
