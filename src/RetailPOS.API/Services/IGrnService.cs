using RetailPOS.API.DTOs.Grn;

namespace RetailPOS.API.Services;

/// <summary>
/// Service interface for GRN (Goods Received Note) business logic
/// </summary>
public interface IGrnService
{
    Task<GrnDto> GetByIdAsync(long id);
    Task<List<GrnDto>> GetAllAsync(long? poId, string? status);
    Task<GrnListDto> SearchAsync(GrnSearchDto searchDto);
    Task<GrnDto> CreateAsync(CreateGrnDto dto, long? userId = null);
    Task<GrnDto> CompleteAsync(long id);
    Task<List<PurchaseOrderForGrnDto>> GetPendingReceiptPOsAsync();
    Task<GrnVarianceDto> GetVarianceAsync(long id);
}
