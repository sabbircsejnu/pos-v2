using RetailPOS.Core.Entities;

namespace RetailPOS.Application.AccountingConfiguration.Abstractions;

public interface IAccountingOutletConfigurationWriteStore
{
    Task<AccountingOutletConfiguration> UpsertAsync(
        long businessId,
        AccountingOutletConfiguration configuration,
        long? actorUserId,
        CancellationToken cancellationToken);
}
