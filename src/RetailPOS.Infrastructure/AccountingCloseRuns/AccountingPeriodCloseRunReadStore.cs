using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingCloseRuns.Abstractions;
using RetailPOS.Application.AccountingCloseRuns.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingCloseRuns;

public sealed class AccountingPeriodCloseRunReadStore(
    RetailPOSDbContext dbContext) : IAccountingPeriodCloseRunReadStore
{
    public async Task<IReadOnlyList<AccountingPeriodCloseRunReadModel>> ListAsync(
        long businessId,
        long? accountingPeriodId,
        CancellationToken cancellationToken)
    {
        var runs = await dbContext.AccountingPeriodCloseRuns
            .AsNoTracking()
            .Include(run => run.GateSnapshots)
            .Where(run => run.BusinessId == businessId
                && (!accountingPeriodId.HasValue
                    || run.AccountingPeriodId == accountingPeriodId.Value))
            .OrderByDescending(run => run.AccountingPeriodId)
            .ThenByDescending(run => run.RunNumber)
            .ToListAsync(cancellationToken);
        var approvals = await GetApprovalsAsync(
            runs.Select(run => run.Id).ToArray(),
            cancellationToken);
        return runs.Select(run => Map(run, approvals)).ToList();
    }

    public async Task<AccountingPeriodCloseRunReadModel> GetAsync(
        long businessId,
        long id,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.AccountingPeriodCloseRuns
            .AsNoTracking()
            .Include(value => value.GateSnapshots)
            .SingleOrDefaultAsync(
                value => value.Id == id && value.BusinessId == businessId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                "Accounting period close run not found.");
        var approvals = await GetApprovalsAsync([run.Id], cancellationToken);
        return Map(run, approvals);
    }

    private async Task<Dictionary<long, AccountingCloseExceptionApproval>> GetApprovalsAsync(
        long[] runIds,
        CancellationToken cancellationToken)
    {
        if (runIds.Length == 0)
        {
            return [];
        }

        var runIdList = runIds.ToList();
        return await dbContext.AccountingCloseExceptionApprovals
            .AsNoTracking()
            .Where(approval => runIdList.Contains(approval.AccountingPeriodCloseRunId))
            .ToDictionaryAsync(
                approval => approval.AccountingCloseGateSnapshotId,
                cancellationToken);
    }

    private static AccountingPeriodCloseRunReadModel Map(
        AccountingPeriodCloseRun run,
        IReadOnlyDictionary<long, AccountingCloseExceptionApproval> approvals) =>
        new(
            run.Id,
            run.AccountingPeriodId,
            run.RunNumber,
            run.Status,
            run.EvaluationSequence,
            run.PreparedBy,
            run.PreparedAt,
            run.ApprovedBy,
            run.ApprovedAt,
            run.ExecutedBy,
            run.ExecutedAt,
            run.ExecutionReason,
            run.ReopenedBy,
            run.ReopenedAt,
            run.ReopenReason,
            run.GateSnapshots
                .OrderBy(snapshot => snapshot.EvaluationSequence)
                .ThenBy(snapshot => snapshot.GateCode)
                .Select(snapshot =>
                {
                    var hasApproval = approvals.TryGetValue(
                        snapshot.Id,
                        out var approval);
                    return new AccountingCloseGateSnapshotReadModel(
                        snapshot.Id,
                        snapshot.EvaluationSequence,
                        snapshot.GateCode,
                        snapshot.Status,
                        snapshot.ExpectedAmount,
                        snapshot.ActualAmount,
                        snapshot.Details,
                        snapshot.IsHistoricalCutoverLimitation,
                        snapshot.EvaluatedAt,
                        hasApproval,
                        approval?.Reason,
                        approval?.ApprovedBy,
                        approval?.ApprovedAt);
                })
                .ToList());
}
