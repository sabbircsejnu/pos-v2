using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RetailPOS.Application;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Infrastructure.AccountingConfigurations;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Infrastructure;

internal static class AccountingConfigurationServiceTestFactory
{
    public static AccountingConfigurationService Create(
        RetailPOSDbContext dbContext,
        ITenantAccessService tenantAccess,
        ILogger<AccountingConfigurationService> logger,
        IAuditService? auditService = null)
    {
        var provisioner = new AccountingConfigurationProvisioner(
            dbContext,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountingConfigurationProvisioner>.Instance);
        IAccountingConfigurationInitializationStore initializationStore =
            new AccountingConfigurationInitializationStore(provisioner);

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(dbContext);
        services.AddSingleton(tenantAccess);
        services.AddSingleton<IAccountingConfigurationTenantScope>(
            new AccountingConfigurationTenantScopeAdapter(tenantAccess));
        services.AddSingleton(initializationStore);
        services.AddSingleton<IAccountingPostingAccountReadStore, AccountingPostingAccountReadStore>();
        services.AddSingleton<IAccountingOutletConfigurationReadStore, AccountingOutletConfigurationReadStore>();
        var serviceProvider = services.BuildServiceProvider();

        return new AccountingConfigurationService(serviceProvider.GetRequiredService<ISender>());
    }
}
