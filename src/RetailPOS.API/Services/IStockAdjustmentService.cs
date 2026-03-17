using RetailPOS.API.DTOs.StockAdjustment;

namespace RetailPOS.API.Services;

/// <summary>
/// Interface for Stock Adjustment service operations
/// </summary>
public interface IStockAdjustmentService
{
    Task<StockAdjustmentDto> GetByIdAsync(long id);
    Task<List<StockAdjustmentDto>> GetAllAsync(long? locationId = null, string? locationType = null, long? variantId = null);
    Task<StockAdjustmentListDto> SearchAsync(StockAdjustmentSearchDto searchDto);
    Task<List<StockAdjustmentDto>> GetHistoryAsync(long variantId, long locationId);
    Task<StockAdjustmentDto> CreateAsync(CreateStockAdjustmentDto dto, long adjustedBy);
}
