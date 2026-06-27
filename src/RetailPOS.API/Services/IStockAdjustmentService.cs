using RetailPOS.API.DTOs.StockAdjustment;

namespace RetailPOS.API.Services;

/// <summary>
/// Interface for Stock Adjustment service operations
/// </summary>
public interface IStockAdjustmentService
{
    Task<StockAdjustmentDto> GetByIdAsync(long id);
    Task<List<StockAdjustmentDto>> GetAllAsync(long? locationId = null, string? locationType = null, long? variantId = null, string? status = null);
    Task<StockAdjustmentListDto> SearchAsync(StockAdjustmentSearchDto searchDto);
    Task<List<StockAdjustmentDto>> GetHistoryAsync(long variantId, long locationId);
    Task<StockAdjustmentDto> CreateAsync(CreateStockAdjustmentDto dto, long adjustedBy);
    Task<StockAdjustmentDto> CreateBatchAsync(CreateStockAdjustmentBatchDto dto, long adjustedBy);
    Task<StockAdjustmentDto> UpdateAsync(long id, UpdateStockAdjustmentDto dto, long updatedBy);
    Task DeleteAsync(long id, long deletedBy);
    Task<StockAdjustmentDto> SubmitAsync(long id, long submittedBy);
    Task<StockAdjustmentDto> ApproveAsync(long id, long approvedBy);
    Task<StockAdjustmentDto> RejectAsync(long id, long rejectedBy, string reason);
    Task<StockAdjustmentDto> CancelAsync(long id, long cancelledBy);
}
