using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IStockRequisitionRepository
{
    Task<StockRequisition?> GetByIdAsync(long id);
    Task<IEnumerable<StockRequisition>> GetAllAsync(string? status = null, long? requestingLocationId = null, long? sourceLocationId = null);
    Task<(IEnumerable<StockRequisition>, int)> SearchAsync(
        string? status,
        long? requestingLocationId,
        string? requestingLocationType,
        long? sourceLocationId,
        string? sourceLocationType,
        int pageNumber,
        int pageSize);
    Task<StockRequisition> CreateAsync(StockRequisition requisition);
    Task<StockRequisition> UpdateAsync(StockRequisition requisition);
}
