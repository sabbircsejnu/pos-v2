using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.BankReconciliation;

public sealed class BankReconciliationSessionCommandStore(
    RetailPOSDbContext dbContext) : IBankReconciliationSessionCommandStore
{
    public async Task<BankReconciliationSessionReadModel> CreateAsync(
        long businessId,
        long actorUserId,
        CreateBankReconciliationSessionInput input,
        CancellationToken cancellationToken)
    {
        if (input.PeriodEnd.Date < input.PeriodStart.Date)
        {
            throw new InvalidOperationException(
                "Reconciliation period end must be on or after start.");
        }

        var bank = await dbContext.BankAccounts
            .SingleOrDefaultAsync(
                account => account.BusinessId == businessId
                    && account.Id == input.BankAccountId
                    && account.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Active BankAccount not found in this business.");
        if (bank.ReconciliationStartDate.HasValue
            && input.PeriodStart.Date < bank.ReconciliationStartDate.Value.Date)
        {
            throw new InvalidOperationException(
                "Reconciliation cannot begin before the approved BankAccount reconciliation start date.");
        }

        var overlaps = await dbContext.BankReconciliationSessions
            .AnyAsync(
                session => session.BusinessId == businessId
                    && session.BankAccountId == input.BankAccountId
                    && session.Status != BankReconciliationSessionStatuses.ReadyForClose
                    && session.PeriodStart <= input.PeriodEnd.Date
                    && session.PeriodEnd >= input.PeriodStart.Date,
                cancellationToken);
        if (overlaps)
        {
            throw new InvalidOperationException(
                "An active reconciliation session overlaps this period.");
        }

        var imports = await dbContext.BankStatementImports
            .AsNoTracking()
            .Where(import =>
                import.BusinessId == businessId
                && import.BankAccountId == input.BankAccountId
                && import.Status == BankStatementImportStatuses.Imported)
            .Where(import => import.Lines.Any(line =>
                line.TransactionDate >= input.PeriodStart.Date
                && line.TransactionDate < input.PeriodEnd.Date.AddDays(1)))
            .ToListAsync(cancellationToken);
        var statementOpening = imports
            .Select(import => import.OpeningBalance)
            .FirstOrDefault(balance => balance.HasValue);
        var statementClosing = imports
            .Select(import => import.ClosingBalance)
            .LastOrDefault(balance => balance.HasValue);
        var glOpening = await dbContext.JournalEntryLines
            .Where(line =>
                line.JournalEntry.BusinessId == businessId
                && line.AccountId == bank.ChartAccountId
                && line.JournalEntry.Status == JournalEntryStatuses.Posted
                && line.JournalEntry.PostingDate < input.PeriodStart.Date)
            .SumAsync(
                line => line.DebitAmount - line.CreditAmount,
                cancellationToken);

        var session = new BankReconciliationSession
        {
            BusinessId = businessId,
            BankAccountId = input.BankAccountId,
            PeriodStart = input.PeriodStart.Date,
            PeriodEnd = input.PeriodEnd.Date,
            StatementOpeningBalance = statementOpening,
            StatementClosingBalance = statementClosing,
            GlOpeningBalance = glOpening,
            Status = BankReconciliationSessionStatuses.Open,
            PreparedBy = actorUserId,
            PreparedAt = DateTime.UtcNow,
            CreatedBy = actorUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = actorUserId,
            UpdatedAt = DateTime.UtcNow
        };
        dbContext.BankReconciliationSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BankReconciliationSessionReadModel(
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
}
