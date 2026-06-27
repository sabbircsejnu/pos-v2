using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IBarcodePrintHistoryRepository
{
    Task<BarcodePrintHistory> CreateAsync(BarcodePrintHistory history);
    Task<BarcodePrintHistory?> GetByIdAsync(long id);
    Task<(List<BarcodePrintHistory> Rows, int TotalCount)> SearchAsync(
        long businessId,
        long? templateId,
        long? printedByUserId,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize);
}
