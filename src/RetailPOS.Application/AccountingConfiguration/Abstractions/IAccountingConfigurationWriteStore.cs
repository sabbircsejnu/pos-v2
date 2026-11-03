using RetailPOS.Core.Entities;
using AccountingConfigurationEntity = RetailPOS.Core.Entities.AccountingConfiguration;

namespace RetailPOS.Application.AccountingConfiguration.Abstractions;

public interface IAccountingConfigurationWriteStore
{
    Task<AccountingConfigurationEntity> UpdateBusinessConfigurationAsync(
        long businessId,
        AccountingConfigurationEntity incoming,
        long? actorUserId,
        CancellationToken cancellationToken);
}
