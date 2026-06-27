using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Interface for Stock Adjustment repository operations
/// </summary>
public interface IStockAdjustmentRepository
{
    Task<StockAdjustment?> GetByIdAsync(long id);
    Task<IEnumerable<StockAdjustment>> GetAllAsync(long? locationId = null, string? locationType = null, long? variantId = null, string? status = null);
    Task<(IEnumerable<StockAdjustment>, int)> SearchAsync(
        long? locationId,
        string? locationType,
        long? variantId,
        string? status,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize);
    Task<IEnumerable<StockAdjustment>> GetHistoryAsync(long variantId, long locationId);
    Task<StockAdjustment> CreateAsync(StockAdjustment adjustment);
}
