using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface ISaleRepository
{
    Task<Sale?> GetByIdAsync(long id);
    Task<(IEnumerable<Sale>, int)> SearchAsync(
        long? outletId, long? cashierId, long? customerId,
        DateTime? startDate, DateTime? endDate, string? status,
        int pageNumber, int pageSize);
    Task<IEnumerable<Sale>> GetTodaysSalesAsync(long? outletId = null);
    Task<Sale> CreateAsync(Sale sale);
    Task<Sale> UpdateAsync(Sale sale);
}
