using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Transaction data access operations
/// </summary>
public class TransactionRepository : ITransactionRepository
{
    private readonly RetailPOSDbContext _context;

    public TransactionRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Transaction?> GetByIdAsync(long id)
    {
        return await _context.Transactions
            .Include(t => t.Account)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<(IEnumerable<Transaction>, int)> SearchAsync(
        long? accountId, DateTime? startDate, DateTime? endDate, string? type, int pageNumber, int pageSize)
    {
        var query = _context.Transactions
            .Include(t => t.Account)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId.Value);

        if (startDate.HasValue)
            query = query.Where(t => t.TransactionDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(t => t.TransactionDate <= endDate.Value);

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(t => t.Type == type.ToLower());

        var totalCount = await query.CountAsync();

        var transactions = await query
            .OrderByDescending(t => t.TransactionDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (transactions, totalCount);
    }

    public async Task<IEnumerable<Transaction>> GetByAccountAsync(long accountId)
    {
        return await _context.Transactions
            .Include(t => t.Account)
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync();
    }

    public async Task<Transaction> CreateAsync(Transaction transaction)
    {
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(transaction.Id))!;
    }
}
