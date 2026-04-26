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
    /// <summary>Returns POs with status "approved" or "partial" that still have outstanding qty.</summary>
    Task<List<PurchaseOrderForGrnDto>> GetPendingReceiptPOsAsync();
    /// <summary>Variance for a single GRN (ordered qty on that GRN vs received qty).</summary>
    Task<GrnVarianceDto> GetVarianceAsync(long id);
    /// <summary>Cumulative variance for a whole PO across all its GRNs.</summary>
    Task<GrnPoVarianceDto> GetPoVarianceAsync(long poId);  // NEW
}
