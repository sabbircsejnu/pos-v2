using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(long id);
    Task<IEnumerable<Account>> GetAllAsync(string? type = null);
    Task<Account> CreateAsync(Account account);
    Task<Account> UpdateAsync(Account account);
    Task<bool> DeleteAsync(long id);
    Task<decimal> GetBalanceAsync(long id);
    Task<bool> HasTransactionsAsync(long id);
}
