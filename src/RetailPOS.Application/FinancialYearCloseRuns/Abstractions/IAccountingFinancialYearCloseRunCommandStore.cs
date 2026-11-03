namespace RetailPOS.Application.FinancialYearCloseRuns.Abstractions;

public interface IAccountingFinancialYearCloseRunCommandStore
{
    Task<long> PrepareAsync(
        long businessId,
        long financialYearId,
        long actorUserId,
        CancellationToken cancellationToken);

    Task ApproveAsync(
        long businessId,
        long accountingFinancialYearCloseRunId,
        long actorUserId,
        CancellationToken cancellationToken);

    Task ExecuteAsync(
        long businessId,
        long accountingFinancialYearCloseRunId,
        long actorUserId,
        CancellationToken cancellationToken);

    Task ReopenAsync(
        long businessId,
        long accountingFinancialYearCloseRunId,
        string? reason,
        DateTime postingDate,
        long actorUserId,
        CancellationToken cancellationToken);
}
