using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IBillRepository
{
    Task<Bill?> GetByIdAsync(long id);
    Task<(IEnumerable<Bill>, int)> SearchAsync(string? status, long? supplierId, int pageNumber, int pageSize);
    Task<Bill> CreateAsync(Bill bill);
    Task<Bill> UpdateAsync(Bill bill);
    Task<IEnumerable<Bill>> GetOverdueAsync();
    Task<(decimal totalUnpaid, decimal totalOverdue, int unpaidCount, int overdueCount)> GetSummaryAsync();
}
