using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.AccountingConfigurations;

public sealed class AccountingConfigurationInitializationStore(
    AccountingConfigurationProvisioner provisioner) : IAccountingConfigurationInitializationStore
{
    public Task<AccountingConfiguration> EnsureInitializedAsync(
        long businessId,
        long? actorUserId,
        CancellationToken cancellationToken) =>
        provisioner.EnsureInitializedAsync(businessId, actorUserId, cancellationToken);
}
