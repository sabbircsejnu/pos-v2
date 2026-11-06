using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.ChartAccounts;
using RetailPOS.Application.ChartAccounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ChartAccounts;

public sealed class ChartAccountReadStore(RetailPOSDbContext dbContext)
    : IChartAccountReadStore
{
    public async Task<IReadOnlyList<ChartAccountReadModel>> GetAllAsync(
        long businessId,
        string? query,
        string? accountType,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        var accounts = dbContext.ChartAccounts
            .AsNoTracking()
            .Where(account => account.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLower();
            accounts = accounts.Where(account =>
                account.AccountCode.ToLower().Contains(term)
                || account.AccountName.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(accountType))
        {
            var type = accountType.Trim();
            accounts = accounts.Where(account => account.AccountType == type);
        }

        if (isActive.HasValue)
        {
            accounts = accounts.Where(account => account.IsActive == isActive.Value);
        }

        var rows = await accounts
            .OrderBy(account => account.DisplayOrder)
            .ThenBy(account => account.AccountCode)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToArray();
    }

    public async Task<ChartAccountReadModel?> GetByIdAsync(
        long businessId,
        long accountId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.ChartAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.Id == accountId && row.BusinessId == businessId,
                cancellationToken);

        return account is null ? null : Map(account);
    }

    private static ChartAccountReadModel Map(ChartAccount account) =>
        new(
            account.Id,
            account.AccountCode,
            account.AccountName,
            account.AccountType,
            account.NormalBalanceType,
            account.ParentAccountId,
            account.IsSystemAccount,
            account.IsControlAccount,
            account.AllowManualPosting,
            account.IsActive,
            account.DisplayOrder,
            account.IsExpensePaymentAccount);
}
