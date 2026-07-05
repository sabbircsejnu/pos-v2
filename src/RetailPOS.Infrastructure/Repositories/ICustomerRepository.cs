using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(long id);
    Task<(IEnumerable<Customer>, int)> SearchAsync(string? query, int pageNumber, int pageSize);
    Task<IEnumerable<Customer>> QuickSearchAsync(string query, int limit = 10);
    Task<Customer?> GetByCodeAsync(string customerCode);
    Task<Customer?> GetByPhoneAsync(string phone);
    Task<Customer?> GetByEmailAsync(string email);
    Task<Customer> CreateAsync(Customer customer);
    Task<Customer> UpdateAsync(Customer customer);
    Task<bool> DeleteAsync(long id);
}
