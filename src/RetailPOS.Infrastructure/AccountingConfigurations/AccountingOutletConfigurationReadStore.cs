using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingConfigurations;

public sealed class AccountingOutletConfigurationReadStore(
    RetailPOSDbContext dbContext) : IAccountingOutletConfigurationReadStore
{
    public async Task<IReadOnlyList<AccountingOutletConfiguration>> GetForBusinessAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        return await dbContext.AccountingOutletConfigurations
            .AsNoTracking()
            .Where(configuration => configuration.BusinessId == businessId)
            .OrderBy(configuration => configuration.OutletId)
            .ToListAsync(cancellationToken);
    }

    public Task<AccountingOutletConfiguration?> GetForOutletAsync(
        long businessId,
        long outletId,
        CancellationToken cancellationToken)
    {
        return dbContext.AccountingOutletConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                configuration =>
                    configuration.BusinessId == businessId &&
                    configuration.OutletId == outletId,
                cancellationToken);
    }
}
