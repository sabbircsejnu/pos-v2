using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingCloseRuns;

public sealed class AccountingPeriodCloseRunCommandStore(
    RetailPOSDbContext dbContext,
    IAccountingPeriodCloseFinancialGateEvaluator financialGateEvaluator,
    IAuditService auditService) : IAccountingPeriodCloseRunCommandStore
{
    public async Task<long> PrepareAsync(
        long businessId,
        long accountingPeriodId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        long runId;
        var preparedRunNumber = 0;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken))
        {
            try
            {
                var period = await dbContext.AccountingPeriods
                    .Include(value => value.FinancialYear)
                    .SingleOrDefaultAsync(
                        value => value.Id == accountingPeriodId
                            && value.FinancialYear.BusinessId == businessId,
                        cancellationToken)
                    ?? throw new KeyNotFoundException(
                        "Accounting period not found.");
                if (period.Status == AccountingPeriodStatuses.Closed)
                {
                    throw new InvalidOperationException(
                        "Closed accounting periods must be reopened before another close run can be prepared.");
                }

                var active = await dbContext.AccountingPeriodCloseRuns
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        run => run.BusinessId == businessId
                            && run.AccountingPeriodId == period.Id
                            && (run.Status == AccountingPeriodCloseRunStatuses.Prepared
                                || run.Status == AccountingPeriodCloseRunStatuses.Approved),
                        cancellationToken);
                if (active is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return active.Id;
                }

                var nextRun = (await dbContext.AccountingPeriodCloseRuns
                    .Where(run => run.BusinessId == businessId
                        && run.AccountingPeriodId == period.Id)
                    .MaxAsync(run => (int?)run.RunNumber, cancellationToken) ?? 0) + 1;
                var now = DateTime.UtcNow;
                var run = new AccountingPeriodCloseRun
                {
                    BusinessId = businessId,
                    AccountingPeriodId = period.Id,
                    RunNumber = nextRun,
                    EvaluationSequence = 1,
                    PreparedBy = actorUserId,
                    PreparedAt = now,
                    CreatedBy = actorUserId,
                    CreatedAt = now
                };
                preparedRunNumber = nextRun;
                dbContext.AccountingPeriodCloseRuns.Add(run);
                if (period.Status == AccountingPeriodStatuses.Open)
                {
                    period.Status = AccountingPeriodStatuses.Locked;
                    period.StatusReason = "Close run prepared";
                    period.LockedAt = now;
                    period.LockedBy = actorUserId;
                    period.UpdatedAt = now;
                    period.UpdatedBy = actorUserId;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                var financialGates = await financialGateEvaluator.EvaluateAsync(
                    period.StartDate.Date,
                    period.EndDate.Date,
                    period.FinancialYearId,
                    period.Id,
                    cancellationToken);
                var snapshots = financialGates.Select(gate =>
                    new AccountingCloseGateSnapshot
                    {
                        AccountingPeriodCloseRunId = run.Id,
                        EvaluationSequence = run.EvaluationSequence,
                        GateCode = gate.GateCode,
                        Status = gate.Status,
                        ExpectedAmount = 0,
                        ActualAmount = gate.ActualAmount,
                        Details = gate.Details,
                        IsHistoricalCutoverLimitation =
                            gate.IsHistoricalCutoverLimitation
                    }).ToList();
                await AddPersistenceGatesAsync(
                    run,
                    period,
                    snapshots,
                    cancellationToken);
                dbContext.AccountingCloseGateSnapshots.AddRange(snapshots);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                runId = run.Id;
            }
            catch (DbUpdateException exception)
                when (IsActiveRunConflict(exception))
            {
                await transaction.RollbackAsync();
                var active = await dbContext.AccountingPeriodCloseRuns
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        run => run.BusinessId == businessId
                            && run.AccountingPeriodId == accountingPeriodId
                            && (run.Status == AccountingPeriodCloseRunStatuses.Prepared
                                || run.Status == AccountingPeriodCloseRunStatuses.Approved),
                        cancellationToken);
                if (active is null)
                {
                    throw;
                }

                return active.Id;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        await auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Accounts,
                Summary = "Accounting period close prepared",
                PrimaryEntity = ("AccountingPeriodCloseRun", runId.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId,
                    ["periodId"] = accountingPeriodId,
                    ["runNumber"] = preparedRunNumber,
                    ["status"] = AccountingPeriodCloseRunStatuses.Prepared,
                    ["evaluationSequence"] = 1
                }
            },
            cancellationToken);
        return runId;
    }

    public async Task ReevaluateAsync(
        long businessId,
        long accountingPeriodCloseRunId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.AccountingPeriodCloseRuns
            .SingleOrDefaultAsync(
                value => value.Id == accountingPeriodCloseRunId
                    && value.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                "Accounting period close run not found.");
        if (run.Status != AccountingPeriodCloseRunStatuses.Prepared)
        {
            throw new InvalidOperationException(
                "Only prepared close runs can be re-evaluated.");
        }

        var period = await dbContext.AccountingPeriods
            .Include(value => value.FinancialYear)
            .SingleOrDefaultAsync(
                value => value.Id == run.AccountingPeriodId
                    && value.FinancialYear.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Accounting period not found.");

        run.EvaluationSequence++;
        var financialGates = await financialGateEvaluator.EvaluateAsync(
            period.StartDate.Date,
            period.EndDate.Date,
            period.FinancialYearId,
            period.Id,
            cancellationToken);
        var snapshots = financialGates.Select(gate =>
            new AccountingCloseGateSnapshot
            {
                AccountingPeriodCloseRunId = run.Id,
                EvaluationSequence = run.EvaluationSequence,
                GateCode = gate.GateCode,
                Status = gate.Status,
                ExpectedAmount = 0,
                ActualAmount = gate.ActualAmount,
                Details = gate.Details,
                IsHistoricalCutoverLimitation =
                    gate.IsHistoricalCutoverLimitation
            }).ToList();
        await AddPersistenceGatesAsync(
            run,
            period,
            snapshots,
            cancellationToken);
        dbContext.AccountingCloseGateSnapshots.AddRange(snapshots);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Accounts,
                Summary = "Accounting period close re-evaluated",
                PrimaryEntity = ("AccountingPeriodCloseRun", run.Id.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId,
                    ["periodId"] = run.AccountingPeriodId,
                    ["runNumber"] = run.RunNumber,
                    ["status"] = run.Status,
                    ["evaluationSequence"] = run.EvaluationSequence
                }
            },
            cancellationToken);
    }

    public async Task ApproveExceptionAsync(
        long businessId,
        long accountingPeriodCloseRunId,
        long gateSnapshotId,
        string? reason,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException(
                "Exception approval reason is required.");
        }

        var run = await dbContext.AccountingPeriodCloseRuns
            .SingleOrDefaultAsync(
                value => value.Id == accountingPeriodCloseRunId
                    && value.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                "Accounting period close run not found.");
        if (run.Status != AccountingPeriodCloseRunStatuses.Prepared)
        {
            throw new InvalidOperationException(
                "Only prepared close runs can receive exception approvals.");
        }

        var gate = await dbContext.AccountingCloseGateSnapshots
            .SingleOrDefaultAsync(
                value => value.Id == gateSnapshotId
                    && value.AccountingPeriodCloseRunId == run.Id,
                cancellationToken)
            ?? throw new KeyNotFoundException("Close gate snapshot not found.");
        if (gate.EvaluationSequence != run.EvaluationSequence
            || gate.Status != AccountingCloseGateStatuses.Blocking
            || !gate.IsHistoricalCutoverLimitation)
        {
            throw new InvalidOperationException(
                "Only the current historical/cutover blocking gate can be approved as an exception.");
        }

        if (await dbContext.AccountingCloseExceptionApprovals
                .AnyAsync(
                    value => value.AccountingCloseGateSnapshotId == gate.Id,
                    cancellationToken))
        {
            throw new InvalidOperationException(
                "This close gate exception is already approved.");
        }

        dbContext.AccountingCloseExceptionApprovals.Add(
            new AccountingCloseExceptionApproval
            {
                AccountingPeriodCloseRunId = run.Id,
                AccountingCloseGateSnapshotId = gate.Id,
                Reason = reason.Trim(),
                ApprovedBy = actorUserId
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Accounts,
                Summary = "Accounting period close exception approved",
                PrimaryEntity = ("AccountingPeriodCloseRun", run.Id.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId,
                    ["periodId"] = run.AccountingPeriodId,
                    ["runNumber"] = run.RunNumber,
                    ["status"] = run.Status,
                    ["evaluationSequence"] = run.EvaluationSequence
                }
            },
            cancellationToken);
    }

    public async Task ApproveAsync(
        long businessId,
        long accountingPeriodCloseRunId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.AccountingPeriodCloseRuns
            .SingleOrDefaultAsync(
                value => value.Id == accountingPeriodCloseRunId
                    && value.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                "Accounting period close run not found.");
        if (run.Status != AccountingPeriodCloseRunStatuses.Prepared)
        {
            throw new InvalidOperationException(
                "Only prepared close runs can be approved.");
        }

        var blockingGates = await dbContext.AccountingCloseGateSnapshots
            .Where(snapshot => snapshot.AccountingPeriodCloseRunId == run.Id
                && snapshot.EvaluationSequence == run.EvaluationSequence
                && snapshot.Status == AccountingCloseGateStatuses.Blocking)
            .Select(snapshot => new
            {
                snapshot.Id,
                snapshot.IsHistoricalCutoverLimitation
            })
            .ToListAsync(cancellationToken);
        var approvedGateIds = await dbContext.AccountingCloseExceptionApprovals
            .Where(approval => approval.AccountingPeriodCloseRunId == run.Id)
            .Select(approval => approval.AccountingCloseGateSnapshotId)
            .ToListAsync(cancellationToken);
        if (blockingGates.Any(gate => !gate.IsHistoricalCutoverLimitation)
            || blockingGates.Any(gate => !approvedGateIds.Contains(gate.Id)))
        {
            throw new InvalidOperationException(
                "Close run has unresolved blocking gates.");
        }

        run.Status = AccountingPeriodCloseRunStatuses.Approved;
        run.ApprovedBy = actorUserId;
        run.ApprovedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Accounts,
                Summary = "Accounting period close approved",
                PrimaryEntity = ("AccountingPeriodCloseRun", run.Id.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId,
                    ["periodId"] = run.AccountingPeriodId,
                    ["runNumber"] = run.RunNumber,
                    ["status"] = run.Status,
                    ["evaluationSequence"] = run.EvaluationSequence
                }
            },
            cancellationToken);
    }

    public async Task<bool> ExecuteAsync(
        long businessId,
        long accountingPeriodCloseRunId,
        string? reason,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        AccountingPeriodCloseRun run;
        try
        {
            run = await dbContext.AccountingPeriodCloseRuns
                .SingleOrDefaultAsync(
                    value => value.Id == accountingPeriodCloseRunId
                        && value.BusinessId == businessId,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    "Accounting period close run not found.");
            if (run.Status == AccountingPeriodCloseRunStatuses.Executed)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            if (run.Status != AccountingPeriodCloseRunStatuses.Approved)
            {
                throw new InvalidOperationException(
                    "Close run must be approved before execution.");
            }

            var period = await dbContext.AccountingPeriods
                .Include(value => value.FinancialYear)
                .SingleOrDefaultAsync(
                    value => value.Id == run.AccountingPeriodId
                        && value.FinancialYear.BusinessId == businessId,
                    cancellationToken)
                ?? throw new KeyNotFoundException("Accounting period not found.");
            if (period.Status != AccountingPeriodStatuses.Locked)
            {
                throw new InvalidOperationException(
                    "Accounting period must remain locked before close execution.");
            }

            run.EvaluationSequence++;
            var financialGates = await financialGateEvaluator.EvaluateAsync(
                period.StartDate.Date,
                period.EndDate.Date,
                period.FinancialYearId,
                period.Id,
                cancellationToken);
            var snapshots = financialGates.Select(gate =>
                new AccountingCloseGateSnapshot
                {
                    AccountingPeriodCloseRunId = run.Id,
                    EvaluationSequence = run.EvaluationSequence,
                    GateCode = gate.GateCode,
                    Status = gate.Status,
                    ExpectedAmount = 0,
                    ActualAmount = gate.ActualAmount,
                    Details = gate.Details,
                    IsHistoricalCutoverLimitation =
                        gate.IsHistoricalCutoverLimitation
                }).ToList();
            await AddPersistenceGatesAsync(
                run,
                period,
                snapshots,
                cancellationToken);
            dbContext.AccountingCloseGateSnapshots.AddRange(snapshots);
            await dbContext.SaveChangesAsync(cancellationToken);

            var blockingGates = await dbContext.AccountingCloseGateSnapshots
                .Where(snapshot => snapshot.AccountingPeriodCloseRunId == run.Id
                    && snapshot.EvaluationSequence == run.EvaluationSequence
                    && snapshot.Status == AccountingCloseGateStatuses.Blocking)
                .Select(snapshot => new
                {
                    snapshot.Id,
                    snapshot.IsHistoricalCutoverLimitation
                })
                .ToListAsync(cancellationToken);
            if (blockingGates.Any(gate => !gate.IsHistoricalCutoverLimitation))
            {
                throw new InvalidOperationException(
                    "Close run gate state changed and requires review before execution.");
            }

            var approvedGateIds = await dbContext.AccountingCloseExceptionApprovals
                .Where(approval => approval.AccountingPeriodCloseRunId == run.Id)
                .Select(approval => approval.AccountingCloseGateSnapshotId)
                .ToListAsync(cancellationToken);
            if (blockingGates.Any(gate => !approvedGateIds.Contains(gate.Id)))
            {
                throw new InvalidOperationException(
                    "Close run gate state changed and requires review before execution.");
            }

            var now = DateTime.UtcNow;
            var executionReason = string.IsNullOrWhiteSpace(reason)
                ? "Approved close run executed"
                : reason.Trim();
            period.Status = AccountingPeriodStatuses.Closed;
            period.StatusReason = executionReason;
            period.ClosedAt = now;
            period.ClosedBy = actorUserId;
            period.UpdatedAt = now;
            period.UpdatedBy = actorUserId;
            run.Status = AccountingPeriodCloseRunStatuses.Executed;
            run.ExecutedBy = actorUserId;
            run.ExecutedAt = now;
            run.ExecutionReason = executionReason;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        await auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Accounts,
                Summary = "Accounting period close executed",
                PrimaryEntity = ("AccountingPeriodCloseRun", run.Id.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId,
                    ["periodId"] = run.AccountingPeriodId,
                    ["runNumber"] = run.RunNumber,
                    ["status"] = run.Status,
                    ["evaluationSequence"] = run.EvaluationSequence
                }
            },
            cancellationToken);
        return true;
    }

    public async Task ReopenAsync(
        long businessId,
        long accountingPeriodCloseRunId,
        string? reason,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Reopen reason is required.");
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        AccountingPeriodCloseRun run;
        try
        {
            run = await dbContext.AccountingPeriodCloseRuns
                .SingleOrDefaultAsync(
                    value => value.Id == accountingPeriodCloseRunId
                        && value.BusinessId == businessId,
                    cancellationToken)
                ?? throw new KeyNotFoundException(
                    "Accounting period close run not found.");
            if (run.Status != AccountingPeriodCloseRunStatuses.Executed)
            {
                throw new InvalidOperationException(
                    "Only an executed close run can reopen its accounting period.");
            }

            var period = await dbContext.AccountingPeriods
                .Include(value => value.FinancialYear)
                .SingleOrDefaultAsync(
                    value => value.Id == run.AccountingPeriodId
                        && value.FinancialYear.BusinessId == businessId,
                    cancellationToken)
                ?? throw new KeyNotFoundException("Accounting period not found.");
            if (period.Status != AccountingPeriodStatuses.Closed)
            {
                throw new InvalidOperationException(
                    "Accounting period is not closed.");
            }

            var now = DateTime.UtcNow;
            var reopenReason = reason.Trim();
            period.Status = AccountingPeriodStatuses.Open;
            period.StatusReason = reopenReason;
            period.ReopenedAt = now;
            period.ReopenedBy = actorUserId;
            period.UpdatedAt = now;
            period.UpdatedBy = actorUserId;
            run.Status = AccountingPeriodCloseRunStatuses.Reopened;
            run.ReopenedAt = now;
            run.ReopenedBy = actorUserId;
            run.ReopenReason = reopenReason;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        await auditService.RecordAsync(
            new AuditEventInput
            {
                ActionType = AuditActionType.Update,
                Module = AuditModule.Accounts,
                Summary = "Accounting period close reopened",
                PrimaryEntity = ("AccountingPeriodCloseRun", run.Id.ToString()),
                Metadata = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId,
                    ["periodId"] = run.AccountingPeriodId,
                    ["runNumber"] = run.RunNumber,
                    ["status"] = run.Status,
                    ["evaluationSequence"] = run.EvaluationSequence
                }
            },
            cancellationToken);
    }

    private async Task AddPersistenceGatesAsync(
        AccountingPeriodCloseRun run,
        AccountingPeriod period,
        ICollection<AccountingCloseGateSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var start = period.StartDate.Date;
        var end = period.EndDate.Date;
        void Add(
            string code,
            string status,
            decimal? actual,
            string details,
            bool historical = false) =>
            snapshots.Add(new AccountingCloseGateSnapshot
            {
                AccountingPeriodCloseRunId = run.Id,
                EvaluationSequence = run.EvaluationSequence,
                GateCode = code,
                Status = status,
                ExpectedAmount = 0,
                ActualAmount = actual,
                Details = details,
                IsHistoricalCutoverLimitation = historical
            });

        var ledgerDifference = await dbContext.JournalEntryLines
            .AsNoTracking()
            .Where(line => line.JournalEntry.BusinessId == run.BusinessId
                && line.JournalEntry.Status == JournalEntryStatuses.Posted
                && line.JournalEntry.PostingDate >= start
                && line.JournalEntry.PostingDate < end.AddDays(1))
            .SumAsync(line => line.DebitAmount - line.CreditAmount, cancellationToken);
        Add(
            "posted_ledger",
            ledgerDifference == 0m
                ? AccountingCloseGateStatuses.Passed
                : AccountingCloseGateStatuses.Blocking,
            ledgerDifference,
            "Posted ledger debits less credits.");

        var bankIncomplete = await dbContext.BankReconciliationSessions
            .AsNoTracking()
            .AnyAsync(session => session.BusinessId == run.BusinessId
                && session.PeriodStart <= end
                && session.PeriodEnd >= start
                && session.Status != BankReconciliationSessionStatuses.Closed,
                cancellationToken);
        Add(
            "bank_reconciliation",
            bankIncomplete
                ? AccountingCloseGateStatuses.Blocking
                : AccountingCloseGateStatuses.Passed,
            bankIncomplete ? 1 : 0,
            "Overlapping bank reconciliation sessions must be closed.");

        var bankExceptions = await dbContext.BankStatementExceptions
            .AsNoTracking()
            .CountAsync(exception => exception.BusinessId == run.BusinessId
                && exception.Status == BankStatementExceptionStatuses.Open
                && exception.StatementLine.TransactionDate >= start
                && exception.StatementLine.TransactionDate < end.AddDays(1),
                cancellationToken);
        Add(
            "bank_exceptions",
            bankExceptions == 0
                ? AccountingCloseGateStatuses.Passed
                : AccountingCloseGateStatuses.Blocking,
            bankExceptions,
            "Open bank statement exceptions in the period.");

        var bankAdjustments = await dbContext.BankStatementAdjustments
            .AsNoTracking()
            .CountAsync(adjustment => adjustment.BusinessId == run.BusinessId
                && adjustment.Status == BankStatementAdjustmentStatuses.Proposed
                && adjustment.StatementLine.TransactionDate >= start
                && adjustment.StatementLine.TransactionDate < end.AddDays(1),
                cancellationToken);
        Add(
            "bank_adjustments",
            bankAdjustments == 0
                ? AccountingCloseGateStatuses.Passed
                : AccountingCloseGateStatuses.Blocking,
            bankAdjustments,
            "Proposed bank adjustments in the period.");

        var drafts = await dbContext.JournalEntries
            .AsNoTracking()
            .CountAsync(entry => entry.BusinessId == run.BusinessId
                && entry.PostingDate >= start
                && entry.PostingDate < end.AddDays(1)
                && entry.JournalType == AccountingJournalTypes.ManualJournal
                && entry.Status != JournalEntryStatuses.Posted,
                cancellationToken);
        if (drafts > 0)
        {
            Add(
                "manual_journal_warning",
                AccountingCloseGateStatuses.Warning,
                drafts,
                "Unposted manual journals are warnings.");
        }

        var expenses = await dbContext.Expenses
            .AsNoTracking()
            .CountAsync(expense => expense.BusinessId == run.BusinessId
                && expense.ExpenseDate >= start
                && expense.ExpenseDate < end.AddDays(1)
                && expense.Status != ExpenseStatuses.Approved,
                cancellationToken);
        if (expenses > 0)
        {
            Add(
                "expense_warning",
                AccountingCloseGateStatuses.Warning,
                expenses,
                "Unapproved expenses are warnings.");
        }

        var income = await dbContext.OtherIncomeEntries
            .AsNoTracking()
            .CountAsync(entry => entry.BusinessId == run.BusinessId
                && entry.IncomeDate >= start
                && entry.IncomeDate < end.AddDays(1)
                && entry.Status != OtherIncomeStatuses.Approved,
                cancellationToken);
        if (income > 0)
        {
            Add(
                "other_income_warning",
                AccountingCloseGateStatuses.Warning,
                income,
                "Unapproved other income is a warning.");
        }
    }

    private static bool IsActiveRunConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && (postgresException.ConstraintName == "ux_accounting_period_close_runs_period_active"
            || postgresException.ConstraintName?.StartsWith(
                "IX_accounting_period_close_runs_business_id_accounting_period",
                StringComparison.Ordinal) == true);
}
