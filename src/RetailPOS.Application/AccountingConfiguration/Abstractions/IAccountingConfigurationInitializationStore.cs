namespace RetailPOS.Application.AccountingConfiguration.Abstractions;

public interface IAccountingConfigurationInitializationStore
{
    Task<RetailPOS.Core.Entities.AccountingConfiguration> EnsureInitializedAsync(
        long businessId,
        long? actorUserId,
        CancellationToken cancellationToken);
}

public interface IAccountingConfigurationTenantScope
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
