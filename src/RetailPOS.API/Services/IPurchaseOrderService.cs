using RetailPOS.API.DTOs.PurchaseOrder;

namespace RetailPOS.API.Services;

/// <summary>
/// Service interface for Purchase Order business logic
/// </summary>
public interface IPurchaseOrderService
{
    /// <summary>
    /// Gets a purchase order by ID
    /// </summary>
    Task<PurchaseOrderDto> GetByIdAsync(long id);

    /// <summary>
    /// Gets all purchase orders with optional filters
    /// </summary>
    Task<List<PurchaseOrderDto>> GetAllAsync(string? status = null, long? supplierId = null, long? warehouseId = null);

    /// <summary>
    /// Searches purchase orders with filters and pagination
    /// </summary>
    Task<PurchaseOrderListDto> SearchAsync(PurchaseOrderSearchDto searchDto);

    /// <summary>
    /// Creates a new purchase order
    /// </summary>
    Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderDto dto, long? userId = null);

    /// <summary>
    /// Updates an existing purchase order (only if draft or pending)
    /// </summary>
    Task<PurchaseOrderDto> UpdateAsync(long id, UpdatePurchaseOrderDto dto);

    /// <summary>
    /// Deletes a purchase order (only if draft or pending with no GRNs)
    /// </summary>
    Task<bool> DeleteAsync(long id);

    /// <summary>
    /// Submits a purchase order for approval (Draft → Pending)
    /// </summary>
    Task<PurchaseOrderDto> SubmitForApprovalAsync(long id);

    /// <summary>
    /// Approves a purchase order (Pending → Approved)
    /// </summary>
    Task<PurchaseOrderDto> ApproveAsync(long id);

    /// <summary>
    /// Rejects a purchase order with reason
    /// </summary>
    Task<PurchaseOrderDto> RejectAsync(long id, string reason);

    /// <summary>
    /// Cancels a purchase order with reason
    /// </summary>
    Task<PurchaseOrderDto> CancelAsync(long id, string reason);

    /// <summary>
    /// Gets purchase orders awaiting approval
    /// </summary>
    Task<List<PurchaseOrderDto>> GetPendingApprovalsAsync();

    /// <summary>
    /// Gets total amount for purchase orders by status and date range
    /// </summary>
    Task<decimal> GetTotalAmountAsync(string? status = null, DateTime? startDate = null, DateTime? endDate = null);
}
