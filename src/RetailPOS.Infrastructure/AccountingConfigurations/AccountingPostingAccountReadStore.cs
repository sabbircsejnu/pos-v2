using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.AccountingConfigurations;

public sealed class AccountingPostingAccountReadStore(
    RetailPOSDbContext dbContext) : IAccountingPostingAccountReadStore
{
    public Task<bool> OutletExistsForBusinessAsync(
        long businessId,
        long outletId,
        CancellationToken cancellationToken)
    {
        return dbContext.Outlets
            .AnyAsync(
                outlet => outlet.Id == outletId && outlet.BusinessId == businessId,
                cancellationToken);
    }

    public Task<AccountingOutletConfiguration?> GetOutletConfigurationAsync(
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

    public Task<ChartAccount?> GetChartAccountAsync(
        long businessId,
        long accountId,
        CancellationToken cancellationToken)
    {
        return dbContext.ChartAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                account => account.BusinessId == businessId && account.Id == accountId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ChartAccount>> GetChartAccountsForBusinessAsync(
        long businessId,
        IReadOnlyCollection<long> accountIds,
        CancellationToken cancellationToken)
    {
        var ids = accountIds.ToList();
        return await dbContext.ChartAccounts
            .AsNoTracking()
            .Where(account =>
                account.BusinessId == businessId &&
                ids.Contains(account.Id))
            .ToListAsync(cancellationToken);
    }
}
