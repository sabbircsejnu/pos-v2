namespace RetailPOS.Application.AccountingCloseRuns.Abstractions;

public interface IAccountingPeriodCloseRunCommandStore
{
    Task<long> PrepareAsync(
        long businessId,
        long accountingPeriodId,
        long actorUserId,
        CancellationToken cancellationToken);

    Task ReevaluateAsync(
        long businessId,
        long accountingPeriodCloseRunId,
        long actorUserId,
        CancellationToken cancellationToken);

    Task ApproveExceptionAsync(
        long businessId,
        long accountingPeriodCloseRunId,
        long gateSnapshotId,
        string? reason,
        long actorUserId,
        CancellationToken cancellationToken);

    Task ApproveAsync(
        long businessId,
        long accountingPeriodCloseRunId,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<bool> ExecuteAsync(
        long businessId,
        long accountingPeriodCloseRunId,
        string? reason,
        long actorUserId,
        CancellationToken cancellationToken);

    Task ReopenAsync(
        long businessId,
        long accountingPeriodCloseRunId,
        string? reason,
        long actorUserId,
        CancellationToken cancellationToken);
}
