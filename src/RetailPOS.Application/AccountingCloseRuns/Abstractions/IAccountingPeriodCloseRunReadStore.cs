using RetailPOS.Application.AccountingCloseRuns.ReadModels;

namespace RetailPOS.Application.AccountingCloseRuns.Abstractions;

public interface IAccountingPeriodCloseRunReadStore
{
    Task<IReadOnlyList<AccountingPeriodCloseRunReadModel>> ListAsync(
        long businessId,
        long? accountingPeriodId,
        CancellationToken cancellationToken);

    Task<AccountingPeriodCloseRunReadModel> GetAsync(
        long businessId,
        long id,
        CancellationToken cancellationToken);
}

public interface IAccountingPeriodCloseTenantScope
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
