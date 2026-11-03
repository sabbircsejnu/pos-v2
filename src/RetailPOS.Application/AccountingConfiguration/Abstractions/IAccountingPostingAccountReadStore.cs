using RetailPOS.Core.Entities;

namespace RetailPOS.Application.AccountingConfiguration.Abstractions;

public interface IAccountingPostingAccountReadStore
{
    Task<bool> OutletExistsForBusinessAsync(
        long businessId,
        long outletId,
        CancellationToken cancellationToken);

    Task<AccountingOutletConfiguration?> GetOutletConfigurationAsync(
        long businessId,
        long outletId,
        CancellationToken cancellationToken);

    Task<ChartAccount?> GetChartAccountAsync(
        long businessId,
        long accountId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChartAccount>> GetChartAccountsForBusinessAsync(
        long businessId,
        IReadOnlyCollection<long> accountIds,
        CancellationToken cancellationToken);
}
