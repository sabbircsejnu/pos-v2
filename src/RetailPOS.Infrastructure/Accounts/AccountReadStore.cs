using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Accounts;
using RetailPOS.Application.Accounts.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Accounts;

public sealed class AccountReadStore(RetailPOSDbContext dbContext)
    : IAccountReadStore
{
    public async Task<IReadOnlyList<AccountReadModel>> GetAllAsync(
        long? businessId,
        string? type,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Accounts.AsNoTracking();

        if (businessId.HasValue)
            query = query.Where(account => account.BusinessId == businessId.Value);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.ToLower();
            query = query.Where(account => account.Type == normalizedType);
        }

        return await query
            .OrderBy(account => account.Type)
            .ThenBy(account => account.Name)
            .Select(account => new AccountReadModel(
                account.Id,
                account.Name,
                account.Type,
                account.Balance))
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountReadModel?> GetByIdAsync(
        long? businessId,
        long accountId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Accounts.AsNoTracking();

        if (businessId.HasValue)
            query = query.Where(account => account.BusinessId == businessId.Value);

        var account = await query
            .Where(account => account.Id == accountId)
            .Select(account => new AccountReadModel(
                account.Id,
                account.Name,
                account.Type,
                account.Balance))
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
            return null;

        var transactions = dbContext.Transactions
            .Where(transaction => transaction.AccountId == accountId);
        if (businessId.HasValue)
            transactions = transactions.Where(
                transaction => transaction.Account.BusinessId == businessId.Value);

        var credits = await transactions
            .Where(transaction => transaction.Type == "credit")
            .SumAsync(transaction => (decimal?)transaction.Amount, cancellationToken) ?? 0m;
        var debits = await transactions
            .Where(transaction => transaction.Type == "debit")
            .SumAsync(transaction => (decimal?)transaction.Amount, cancellationToken) ?? 0m;

        return account with { Balance = credits - debits };
    }
}
