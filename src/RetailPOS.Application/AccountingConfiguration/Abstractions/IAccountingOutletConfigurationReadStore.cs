using RetailPOS.Core.Entities;

namespace RetailPOS.Application.AccountingConfiguration.Abstractions;

public interface IAccountingOutletConfigurationReadStore
{
    Task<IReadOnlyList<AccountingOutletConfiguration>> GetForBusinessAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<AccountingOutletConfiguration?> GetForOutletAsync(
        long businessId,
        long outletId,
        CancellationToken cancellationToken);
}
