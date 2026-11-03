using RetailPOS.Application.FinancialYearCloseRuns.ReadModels;

namespace RetailPOS.Application.FinancialYearCloseRuns.Abstractions;

public interface IAccountingFinancialYearCloseRunReadStore
{
    Task<IReadOnlyList<AccountingFinancialYearCloseRunReadModel>> ListAsync(
        long businessId,
        long? financialYearId,
        CancellationToken cancellationToken);

    Task<AccountingFinancialYearCloseRunReadModel> GetAsync(
        long businessId,
        long id,
        CancellationToken cancellationToken);
}

public interface IAccountingFinancialYearCloseTenantScope
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
