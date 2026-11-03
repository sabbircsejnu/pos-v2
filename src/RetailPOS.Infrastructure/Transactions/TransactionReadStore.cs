using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Transactions;
using RetailPOS.Application.Transactions.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Transactions;

public sealed class TransactionReadStore(RetailPOSDbContext dbContext)
    : ITransactionReadStore
{
    public async Task<(IReadOnlyList<TransactionReadModel> Transactions, int TotalCount)> SearchAsync(
        long? businessId,
        long? accountId,
        DateTime? startDate,
        DateTime? endDate,
        string? type,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Transactions.AsNoTracking();

        if (businessId.HasValue)
            query = query.Where(transaction =>
                transaction.Account.BusinessId == businessId.Value);

        if (accountId.HasValue)
            query = query.Where(transaction =>
                transaction.AccountId == accountId.Value);

        if (startDate.HasValue)
            query = query.Where(transaction =>
                transaction.TransactionDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(transaction =>
                transaction.TransactionDate <= endDate.Value);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.ToLower();
            query = query.Where(transaction => transaction.Type == normalizedType);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var transactions = await query
            .OrderByDescending(transaction => transaction.TransactionDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(transaction => new TransactionReadModel(
                transaction.Id,
                transaction.AccountId,
                transaction.Account.Name,
                transaction.Amount,
                transaction.Type,
                transaction.Description,
                transaction.TransactionDate,
                transaction.ReferenceType,
                transaction.ReferenceId))
            .ToListAsync(cancellationToken);

        return (transactions, totalCount);
    }

    public async Task<TransactionReadModel?> GetByIdAsync(
        long? businessId,
        long transactionId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Transactions.AsNoTracking();
        if (businessId.HasValue)
            query = query.Where(transaction =>
                transaction.Account.BusinessId == businessId.Value);

        return await query
            .Where(transaction => transaction.Id == transactionId)
            .Select(transaction => new TransactionReadModel(
                transaction.Id,
                transaction.AccountId,
                transaction.Account.Name,
                transaction.Amount,
                transaction.Type,
                transaction.Description,
                transaction.TransactionDate,
                transaction.ReferenceType,
                transaction.ReferenceId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TransactionReadModel>?> GetLedgerAsync(
        long? businessId,
        long accountId,
        CancellationToken cancellationToken)
    {
        var accountQuery = dbContext.Accounts.AsNoTracking();
        if (businessId.HasValue)
            accountQuery = accountQuery.Where(account =>
                account.BusinessId == businessId.Value);

        if (!await accountQuery.AnyAsync(
                account => account.Id == accountId,
                cancellationToken))
        {
            return null;
        }

        var query = dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.AccountId == accountId);
        if (businessId.HasValue)
            query = query.Where(transaction =>
                transaction.Account.BusinessId == businessId.Value);

        return await query
            .OrderByDescending(transaction => transaction.TransactionDate)
            .Select(transaction => new TransactionReadModel(
                transaction.Id,
                transaction.AccountId,
                transaction.Account.Name,
                transaction.Amount,
                transaction.Type,
                transaction.Description,
                transaction.TransactionDate,
                transaction.ReferenceType,
                transaction.ReferenceId))
            .ToListAsync(cancellationToken);
    }
}
