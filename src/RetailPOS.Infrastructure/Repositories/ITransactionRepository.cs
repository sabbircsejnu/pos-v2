using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(long id);
    Task<(IEnumerable<Transaction>, int)> SearchAsync(
        long? accountId, DateTime? startDate, DateTime? endDate, string? type, int pageNumber, int pageSize);
    Task<IEnumerable<Transaction>> GetByAccountAsync(long accountId);
    Task<Transaction> CreateAsync(Transaction transaction);
}
