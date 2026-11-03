using System.Data;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.FinancialYearCloseRuns;

public sealed class AccountingFinancialYearCloseRunCommandStore(
    RetailPOSDbContext dbContext,
    IAuditService auditService,
    IAccountingFinancialYearCloseJournalPosting journalPosting)
    : IAccountingFinancialYearCloseRunCommandStore
{
    public async Task<long> PrepareAsync(
        long businessId,
        long financialYearId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        long runId;
        var runNumber = 0;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken))
        {
            try
            {
                var year = await dbContext.FinancialYears
                    .SingleOrDefaultAsync(
                        value => value.Id == financialYearId
                            && value.BusinessId == businessId,
                        cancellationToken)
                    ?? throw new KeyNotFoundException("Financial year not found.");
                if (year.IsClosed)
                {
                    throw new InvalidOperationException(
                        "Closed financial years must be reopened before another close run can be prepared.");
                }

                var active = await dbContext.AccountingFinancialYearCloseRuns
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        value => value.BusinessId == businessId
                            && value.FinancialYearId == year.Id
                            && (value.Status == AccountingFinancialYearCloseRunStatuses.Prepared
                                || value.Status == AccountingFinancialYearCloseRunStatuses.Approved),
                        cancellationToken);
                if (active is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return active.Id;
                }

                var eligibility = await ValidateEligibilityAsync(
                    year,
                    cancellationToken);

                var nextRun = (await dbContext.AccountingFinancialYearCloseRuns
                    .Where(value => value.BusinessId == businessId
                        && value.FinancialYearId == year.Id)
                    .MaxAsync(value => (int?)value.RunNumber, cancellationToken) ?? 0) + 1;
                var run = new AccountingFinancialYearCloseRun
                {
                    BusinessId = businessId,
                    FinancialYearId = year.Id,
                    FinalAccountingPeriodId = eligibility.FinalPeriod.Id,
                    FinalPeriodCloseRunId = eligibility.FinalRun.Id,
                    RunNumber = nextRun,
                    PreparedBy = actorUserId,
                    CreatedBy = actorUserId
                };
                dbContext.AccountingFinancialYearCloseRuns.Add(run);
                runNumber = nextRun;
                await dbContext.SaveChangesAsync(cancellationToken);
                runId = run.Id;
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        await auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Accounts,
                Summary = "Financial year close prepared",
                PrimaryEntity = ("AccountingFinancialYearCloseRun", runId.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId,
                    ["financialYearId"] = financialYearId,
                    ["runNumber"] = runNumber,
                    ["status"] = AccountingFinancialYearCloseRunStatuses.Prepared,
                    ["journalEntryId"] = null,
                    ["reversalJournalEntryId"] = null
                }
            },
            cancellationToken);

        return runId;
    }

    public async Task ApproveAsync(
        long businessId,
        long accountingFinancialYearCloseRunId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.AccountingFinancialYearCloseRuns
            .SingleOrDefaultAsync(
                value => value.Id == accountingFinancialYearCloseRunId
                    && value.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Financial year close run not found.");
        if (run.Status != AccountingFinancialYearCloseRunStatuses.Prepared)
        {
            throw new InvalidOperationException(
                "Only prepared financial year close runs can be approved.");
        }

        var year = await dbContext.FinancialYears
            .SingleOrDefaultAsync(
                value => value.Id == run.FinancialYearId
                    && value.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Financial year not found.");
        await ValidateEligibilityAsync(year, cancellationToken);

        run.Status = AccountingFinancialYearCloseRunStatuses.Approved;
        run.ApprovedBy = actorUserId;
        run.ApprovedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Accounts,
                Summary = "Financial year close approved",
                PrimaryEntity = ("AccountingFinancialYearCloseRun", run.Id.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId,
                    ["financialYearId"] = run.FinancialYearId,
                    ["runNumber"] = run.RunNumber,
                    ["status"] = run.Status,
                    ["journalEntryId"] = run.YearEndJournalEntryId,
                    ["reversalJournalEntryId"] = run.ReversalJournalEntryId
                }
            },
            cancellationToken);
    }

    public async Task ExecuteAsync(
        long businessId,
        long accountingFinancialYearCloseRunId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        AccountingFinancialYearCloseRun run;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken))
        {
            try
            {
                run = await dbContext.AccountingFinancialYearCloseRuns
                    .SingleOrDefaultAsync(
                        value => value.Id == accountingFinancialYearCloseRunId
                            && value.BusinessId == businessId,
                        cancellationToken)
                    ?? throw new KeyNotFoundException(
                        "Financial year close run not found.");
                if (run.Status == AccountingFinancialYearCloseRunStatuses.Executed)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
                if (run.Status != AccountingFinancialYearCloseRunStatuses.Approved)
                {
                    throw new InvalidOperationException(
                        "Financial year close run must be approved before execution.");
                }

                var year = await dbContext.FinancialYears
                    .SingleOrDefaultAsync(
                        value => value.Id == run.FinancialYearId
                            && value.BusinessId == businessId,
                        cancellationToken)
                    ?? throw new KeyNotFoundException("Financial year not found.");
                var eligibility = await ValidateEligibilityAsync(
                    year,
                    cancellationToken);
                if (eligibility.FinalPeriod.Id != run.FinalAccountingPeriodId
                    || eligibility.FinalRun.Id != run.FinalPeriodCloseRunId)
                {
                    throw new InvalidOperationException(
                        "Financial year close eligibility changed and requires a new close run.");
                }

                var journalEntryId = await CreateAndPostClosingJournalAsync(
                    run,
                    year,
                    actorUserId,
                    cancellationToken);
                var now = DateTime.UtcNow;
                eligibility.FinalPeriod.Status = AccountingPeriodStatuses.Closed;
                eligibility.FinalPeriod.StatusReason =
                    "Closed by approved financial year close run";
                eligibility.FinalPeriod.ClosedAt = now;
                eligibility.FinalPeriod.ClosedBy = actorUserId;
                eligibility.FinalPeriod.UpdatedAt = now;
                eligibility.FinalPeriod.UpdatedBy = actorUserId;
                eligibility.FinalRun.Status = AccountingPeriodCloseRunStatuses.Executed;
                eligibility.FinalRun.ExecutedAt = now;
                eligibility.FinalRun.ExecutedBy = actorUserId;
                eligibility.FinalRun.ExecutionReason =
                    "Executed with financial year close";
                year.IsClosed = true;
                year.IsCurrent = false;
                year.ClosedAt = now;
                year.ClosedBy = actorUserId;
                year.UpdatedAt = now;
                year.UpdatedBy = actorUserId;
                run.Status = AccountingFinancialYearCloseRunStatuses.Executed;
                run.ExecutedAt = now;
                run.ExecutedBy = actorUserId;
                run.YearEndJournalEntryId = journalEntryId;
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        await auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Accounts,
                Summary = "Financial year close executed",
                PrimaryEntity = ("AccountingFinancialYearCloseRun", run.Id.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId,
                    ["financialYearId"] = run.FinancialYearId,
                    ["runNumber"] = run.RunNumber,
                    ["status"] = run.Status,
                    ["journalEntryId"] = run.YearEndJournalEntryId,
                    ["reversalJournalEntryId"] = run.ReversalJournalEntryId
                }
            },
            cancellationToken);
    }

    public async Task ReopenAsync(
        long businessId,
        long accountingFinancialYearCloseRunId,
        string? reason,
        DateTime postingDate,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException(
                "Financial year reopen reason is required.");
        }

        AccountingFinancialYearCloseRun run;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken))
        {
            try
            {
                run = await dbContext.AccountingFinancialYearCloseRuns
                    .SingleOrDefaultAsync(
                        value => value.Id == accountingFinancialYearCloseRunId
                            && value.BusinessId == businessId,
                        cancellationToken)
                    ?? throw new KeyNotFoundException(
                        "Financial year close run not found.");
                if (run.Status != AccountingFinancialYearCloseRunStatuses.Executed
                    || !run.YearEndJournalEntryId.HasValue)
                {
                    throw new InvalidOperationException(
                        "Only an executed financial year close run can be reopened.");
                }

                var year = await dbContext.FinancialYears
                    .SingleOrDefaultAsync(
                        value => value.Id == run.FinancialYearId
                            && value.BusinessId == businessId,
                        cancellationToken)
                    ?? throw new KeyNotFoundException("Financial year not found.");
                if (!year.IsClosed)
                    throw new InvalidOperationException("Financial year is not closed.");

                var original = await dbContext.JournalEntries
                    .SingleAsync(
                        value => value.Id == run.YearEndJournalEntryId.Value
                            && value.BusinessId == year.BusinessId,
                        cancellationToken);
                if (original.Status != JournalEntryStatuses.Posted)
                {
                    throw new InvalidOperationException(
                        "The effective YearEndClosing journal must be posted before financial year reopen.");
                }

                var trimmedReason = reason.Trim();
                var reversalJournalEntryId =
                    await journalPosting.ReverseYearEndJournalAsync(
                        businessId,
                        original.Id,
                        trimmedReason,
                        postingDate.Date,
                        actorUserId,
                        cancellationToken);
                var now = DateTime.UtcNow;
                year.IsClosed = false;
                year.ClosedAt = null;
                year.ClosedBy = null;
                year.UpdatedAt = now;
                year.UpdatedBy = actorUserId;
                run.Status = AccountingFinancialYearCloseRunStatuses.Reopened;
                run.ReopenedAt = now;
                run.ReopenedBy = actorUserId;
                run.ReopenReason = trimmedReason;
                run.ReversalJournalEntryId = reversalJournalEntryId;
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        await auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Accounts,
                Summary = "Financial year close reopened with year-end reversal",
                PrimaryEntity = ("AccountingFinancialYearCloseRun", run.Id.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId,
                    ["financialYearId"] = run.FinancialYearId,
                    ["runNumber"] = run.RunNumber,
                    ["status"] = run.Status,
                    ["journalEntryId"] = run.YearEndJournalEntryId,
                    ["reversalJournalEntryId"] = run.ReversalJournalEntryId
                }
            },
            cancellationToken);
    }

    private async Task<(AccountingPeriod FinalPeriod, AccountingPeriodCloseRun FinalRun)> ValidateEligibilityAsync(
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        if (year.IsClosed)
            throw new InvalidOperationException("Financial year is already closed.");

        var periods = await dbContext.AccountingPeriods
            .Where(value => value.FinancialYearId == year.Id)
            .OrderBy(value => value.EndDate)
            .ToListAsync(cancellationToken);
        if (periods.Count == 0)
            throw new InvalidOperationException("Financial year has no accounting periods.");

        var finalPeriod = periods[^1];
        foreach (var period in periods.Where(value => value.Id != finalPeriod.Id))
        {
            var completed = await dbContext.AccountingPeriodCloseRuns
                .AnyAsync(
                    value => value.BusinessId == year.BusinessId
                        && value.AccountingPeriodId == period.Id
                        && value.Status == AccountingPeriodCloseRunStatuses.Executed,
                    cancellationToken);
            if (period.Status != AccountingPeriodStatuses.Closed || !completed)
            {
                throw new InvalidOperationException(
                    "Every non-final accounting period must have an executed Slice 1 close run before financial year close.");
            }
        }

        var finalRun = await dbContext.AccountingPeriodCloseRuns
            .SingleOrDefaultAsync(
                value => value.BusinessId == year.BusinessId
                    && value.AccountingPeriodId == finalPeriod.Id
                    && value.Status == AccountingPeriodCloseRunStatuses.Approved,
                cancellationToken);
        if (finalRun is null)
        {
            throw new InvalidOperationException(
                "Final accounting period requires an approved Slice 1 close run before financial year close.");
        }
        if (finalPeriod.Status != AccountingPeriodStatuses.Locked)
        {
            throw new InvalidOperationException(
                "Final accounting period must remain Locked for controlled year-end posting.");
        }
        return (finalPeriod, finalRun);
    }

    private async Task<long> CreateAndPostClosingJournalAsync(
        AccountingFinancialYearCloseRun run,
        FinancialYear year,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.AccountingConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.BusinessId == year.BusinessId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Accounting configuration is required for financial year close.");
        if (!configuration.RetainedEarningsAccountId.HasValue
            || !configuration.OwnerCapitalAccountId.HasValue
            || !configuration.OwnerDrawingsAccountId.HasValue)
        {
            throw new InvalidOperationException(
                "Retained Earnings, Owner Capital, and Owner Drawings must be configured before financial year close.");
        }

        var accountIds = new[]
        {
            configuration.RetainedEarningsAccountId.Value,
            configuration.OwnerCapitalAccountId.Value,
            configuration.OwnerDrawingsAccountId.Value
        };
        if (accountIds.Distinct().Count() != accountIds.Length)
        {
            throw new InvalidOperationException(
                "Retained Earnings, Owner Capital, and Owner Drawings must be configured as separate accounts.");
        }

        var configuredAccountIds = accountIds.ToList();
        var accounts = await dbContext.ChartAccounts
            .AsNoTracking()
            .Where(value => value.BusinessId == year.BusinessId
                && configuredAccountIds.Contains(value.Id)
                && value.IsActive)
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        if (accounts.Count != accountIds.Length
            || accounts.Values.Any(value => value.AccountType != ChartAccountTypes.Equity))
        {
            throw new InvalidOperationException(
                "Configured retained earnings, owner capital, and owner drawings accounts must be active Equity accounts.");
        }

        var balances = await dbContext.JournalEntryLines
            .AsNoTracking()
            .Where(value => value.JournalEntry.BusinessId == year.BusinessId
                && value.JournalEntry.FinancialYearId == year.Id
                && (value.JournalEntry.Status == JournalEntryStatuses.Posted
                    || (value.JournalEntry.Status == JournalEntryStatuses.Reversed
                        && value.JournalEntry.ReversedByJournalEntryId.HasValue))
                && value.JournalEntry.ReferenceType
                    != AccountingReferenceTypes.FinancialYearClose)
            .Include(value => value.Account)
            .GroupBy(value => new
            {
                value.AccountId,
                value.Account.AccountType
            })
            .Select(group => new
            {
                group.Key.AccountId,
                group.Key.AccountType,
                Debit = group.Sum(value => value.DebitAmount),
                Credit = group.Sum(value => value.CreditAmount)
            })
            .ToListAsync(cancellationToken);
        var lines = new List<FinancialYearCloseJournalLine>();
        foreach (var account in balances.Where(value =>
                     value.AccountType == ChartAccountTypes.Income
                     || value.AccountType == ChartAccountTypes.CostOfGoodsSold
                     || value.AccountType == ChartAccountTypes.Expense))
        {
            var normal = account.AccountType == ChartAccountTypes.Income
                ? account.Credit - account.Debit
                : account.Debit - account.Credit;
            if (normal == 0m)
                continue;
            lines.Add(normal > 0m
                ? new FinancialYearCloseJournalLine(
                    account.AccountId,
                    account.AccountType == ChartAccountTypes.Income ? normal : 0m,
                    account.AccountType == ChartAccountTypes.Income ? 0m : normal,
                    "Financial year closing")
                : new FinancialYearCloseJournalLine(
                    account.AccountId,
                    account.AccountType == ChartAccountTypes.Income ? 0m : -normal,
                    account.AccountType == ChartAccountTypes.Income ? -normal : 0m,
                    "Financial year closing"));
        }

        var drawings = balances.SingleOrDefault(value =>
            value.AccountId == configuration.OwnerDrawingsAccountId.Value);
        var drawingsNormal = drawings is null
            ? 0m
            : drawings.Debit - drawings.Credit;
        if (drawingsNormal != 0m)
        {
            lines.Add(drawingsNormal > 0m
                ? new FinancialYearCloseJournalLine(
                    configuration.OwnerDrawingsAccountId.Value,
                    0m,
                    drawingsNormal,
                    "Owner drawings year-end close")
                : new FinancialYearCloseJournalLine(
                    configuration.OwnerDrawingsAccountId.Value,
                    -drawingsNormal,
                    0m,
                    "Owner drawings year-end close"));
            lines.Add(drawingsNormal > 0m
                ? new FinancialYearCloseJournalLine(
                    configuration.OwnerCapitalAccountId.Value,
                    drawingsNormal,
                    0m,
                    "Owner drawings transfer")
                : new FinancialYearCloseJournalLine(
                    configuration.OwnerCapitalAccountId.Value,
                    0m,
                    -drawingsNormal,
                    "Owner drawings transfer"));
        }

        var difference = lines.Sum(value => value.DebitAmount)
            - lines.Sum(value => value.CreditAmount);
        if (difference != 0m)
        {
            lines.Add(difference > 0m
                ? new FinancialYearCloseJournalLine(
                    configuration.RetainedEarningsAccountId.Value,
                    0m,
                    difference,
                    "Financial year earnings transfer")
                : new FinancialYearCloseJournalLine(
                    configuration.RetainedEarningsAccountId.Value,
                    -difference,
                    0m,
                    "Financial year earnings transfer"));
        }
        if (lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Financial year has no temporary-account or owner-drawings balances to close.");
        }

        var journalEntryId = await journalPosting.CreateOrGetSystemDraftAsync(
            year.BusinessId,
            new FinancialYearCloseJournalDraft(
                AccountingJournalTypes.YearEndClosing,
                year.EndDate.Date,
                year.EndDate.Date,
                year.EndDate.Date,
                AccountingReferenceTypes.FinancialYearClose,
                year.Id.ToString(),
                $"Financial year close {year.YearCode}",
                lines),
            actorUserId,
            $"financial-year-close:{run.Id}",
            cancellationToken);
        return await journalPosting.PostApprovedYearEndClosingAsync(
            year.BusinessId,
            journalEntryId,
            run.Id,
            actorUserId,
            cancellationToken);
    }
}
