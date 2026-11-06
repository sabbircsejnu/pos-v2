using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Accounting;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Expenses;

/// <summary>
/// Builds the funding-account classification for one expense location. An outlet sees its own overrides and bank
/// accounts as well as the Business ones; a warehouse or business-level expense sees only the Business ones.
/// </summary>
internal static class ExpensePaymentAccountMapReader
{
    public static async Task<ExpensePaymentAccountMap> LoadAsync(
        RetailPOSDbContext dbContext,
        long businessId,
        long? outletId,
        CancellationToken cancellationToken)
    {
        var business = await dbContext.AccountingConfigurations
            .AsNoTracking()
            .Where(configuration => configuration.BusinessId == businessId)
            .Select(configuration => new
            {
                configuration.CashAccountId,
                configuration.PettyCashAccountId,
                configuration.DefaultBankAccountId
            })
            .FirstOrDefaultAsync(cancellationToken);

        var outlet = outletId.HasValue
            ? await dbContext.AccountingOutletConfigurations
                .AsNoTracking()
                .Where(configuration => configuration.BusinessId == businessId && configuration.OutletId == outletId.Value)
                .Select(configuration => new
                {
                    configuration.OutletCashAccountId,
                    configuration.OutletPettyCashAccountId,
                    configuration.OutletBankAccountId
                })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var registeredBankAccountIds = await dbContext.BankAccounts
            .AsNoTracking()
            .Where(bank => bank.BusinessId == businessId
                && bank.IsActive
                && (bank.OutletId == null || bank.OutletId == outletId))
            .Select(bank => bank.ChartAccountId)
            .ToListAsync(cancellationToken);

        var cash = new HashSet<long>();
        AddIfPresent(cash, business?.CashAccountId, business?.PettyCashAccountId, outlet?.OutletCashAccountId, outlet?.OutletPettyCashAccountId);
        var bank = new HashSet<long>(registeredBankAccountIds);
        AddIfPresent(bank, business?.DefaultBankAccountId, outlet?.OutletBankAccountId);

        var drawerCash = outletId.HasValue ? outlet?.OutletCashAccountId ?? business?.CashAccountId : null;
        return new ExpensePaymentAccountMap(drawerCash, cash, bank);
    }

    private static void AddIfPresent(HashSet<long> target, params long?[] accountIds)
    {
        foreach (var accountId in accountIds)
        {
            if (accountId.HasValue)
                target.Add(accountId.Value);
        }
    }
}
