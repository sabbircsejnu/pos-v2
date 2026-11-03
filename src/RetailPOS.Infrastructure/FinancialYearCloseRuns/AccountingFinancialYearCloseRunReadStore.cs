using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;
using RetailPOS.Application.FinancialYearCloseRuns.ReadModels;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.FinancialYearCloseRuns;

public sealed class AccountingFinancialYearCloseRunReadStore(
    RetailPOSDbContext dbContext) : IAccountingFinancialYearCloseRunReadStore
{
    public async Task<IReadOnlyList<AccountingFinancialYearCloseRunReadModel>> ListAsync(
        long businessId,
        long? financialYearId,
        CancellationToken cancellationToken)
    {
        var runs = await dbContext.AccountingFinancialYearCloseRuns
            .AsNoTracking()
            .Where(run => run.BusinessId == businessId
                && (!financialYearId.HasValue
                    || run.FinancialYearId == financialYearId.Value))
            .OrderByDescending(run => run.FinancialYearId)
            .ThenByDescending(run => run.RunNumber)
            .Select(run => new AccountingFinancialYearCloseRunReadModel(
                run.Id,
                run.FinancialYearId,
                run.FinalAccountingPeriodId,
                run.FinalPeriodCloseRunId,
                run.RunNumber,
                run.Status,
                run.PreparedBy,
                run.PreparedAt,
                run.ApprovedBy,
                run.ApprovedAt,
                run.ExecutedBy,
                run.ExecutedAt,
                run.YearEndJournalEntryId,
                run.YearEndJournalEntry == null
                    ? null
                    : run.YearEndJournalEntry.JournalNumber,
                run.ReversalJournalEntryId,
                run.ReopenedBy,
                run.ReopenedAt,
                run.ReopenReason))
            .ToListAsync(cancellationToken);
        return runs;
    }

    public async Task<AccountingFinancialYearCloseRunReadModel> GetAsync(
        long businessId,
        long id,
        CancellationToken cancellationToken) =>
        await dbContext.AccountingFinancialYearCloseRuns
            .AsNoTracking()
            .Where(run => run.Id == id && run.BusinessId == businessId)
            .Select(run => new AccountingFinancialYearCloseRunReadModel(
                run.Id,
                run.FinancialYearId,
                run.FinalAccountingPeriodId,
                run.FinalPeriodCloseRunId,
                run.RunNumber,
                run.Status,
                run.PreparedBy,
                run.PreparedAt,
                run.ApprovedBy,
                run.ApprovedAt,
                run.ExecutedBy,
                run.ExecutedAt,
                run.YearEndJournalEntryId,
                run.YearEndJournalEntry == null
                    ? null
                    : run.YearEndJournalEntry.JournalNumber,
                run.ReversalJournalEntryId,
                run.ReopenedBy,
                run.ReopenedAt,
                run.ReopenReason))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("Financial year close run not found.");
}
