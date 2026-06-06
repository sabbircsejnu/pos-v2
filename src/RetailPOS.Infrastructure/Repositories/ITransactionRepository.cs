using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(long id, long? businessId = null);
    Task<(IEnumerable<Transaction>, int)> SearchAsync(
        long? accountId, DateTime? startDate, DateTime? endDate, string? type, int pageNumber, int pageSize, long? businessId = null);
    Task<IEnumerable<Transaction>> GetByAccountAsync(long accountId, long? businessId = null);
    Task<Transaction> CreateAsync(Transaction transaction);
}
