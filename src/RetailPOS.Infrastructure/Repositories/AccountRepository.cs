using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Account data access operations
/// </summary>
public class AccountRepository : IAccountRepository
{
    private readonly RetailPOSDbContext _context;

    public AccountRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Account?> GetByIdAsync(long id, long? businessId = null)
    {
        var query = _context.Accounts.AsQueryable();

        if (businessId.HasValue)
            query = query.Where(a => a.BusinessId == businessId.Value);

        return await query.FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<IEnumerable<Account>> GetAllAsync(string? type = null, long? businessId = null)
    {
        var query = _context.Accounts.AsQueryable();

        if (businessId.HasValue)
            query = query.Where(a => a.BusinessId == businessId.Value);

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(a => a.Type == type.ToLower());

        return await query.OrderBy(a => a.Type).ThenBy(a => a.Name).ToListAsync();
    }

    public async Task<Account> CreateAsync(Account account)
    {
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    public async Task<Account> UpdateAsync(Account account)
    {
        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();
        return account;
    }

    public async Task<bool> DeleteAsync(long id, long? businessId = null)
    {
        var query = _context.Accounts.AsQueryable();
        if (businessId.HasValue)
            query = query.Where(a => a.BusinessId == businessId.Value);

        var account = await query.FirstOrDefaultAsync(a => a.Id == id);
        if (account == null) return false;

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<decimal> GetBalanceAsync(long id, long? businessId = null)
    {
        var query = _context.Transactions
            .Where(t => t.AccountId == id)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(t => t.Account.BusinessId == businessId.Value);

        var credits = await query
            .Where(t => t.Type == "credit")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        var debits = await query
            .Where(t => t.Type == "debit")
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        return credits - debits;
    }

    public async Task<bool> HasTransactionsAsync(long id, long? businessId = null)
    {
        var query = _context.Transactions
            .Where(t => t.AccountId == id)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(t => t.Account.BusinessId == businessId.Value);

        return await query.AnyAsync();
    }
}
