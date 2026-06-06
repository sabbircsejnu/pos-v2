using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(long id, long? businessId = null);
    Task<IEnumerable<Account>> GetAllAsync(string? type = null, long? businessId = null);
    Task<Account> CreateAsync(Account account);
    Task<Account> UpdateAsync(Account account);
    Task<bool> DeleteAsync(long id, long? businessId = null);
    Task<decimal> GetBalanceAsync(long id, long? businessId = null);
    Task<bool> HasTransactionsAsync(long id, long? businessId = null);
}
