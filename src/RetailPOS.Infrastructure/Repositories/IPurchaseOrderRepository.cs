using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository interface for Purchase Order data access operations
/// </summary>
public interface IPurchaseOrderRepository
{
    /// <summary>
    /// Gets a purchase order by ID with related entities
    /// </summary>
    Task<PurchaseOrder?> GetByIdAsync(long id);

    /// <summary>
    /// Gets all purchase orders with optional filters
    /// </summary>
    Task<IEnumerable<PurchaseOrder>> GetAllAsync(string? status = null, long? supplierId = null, long? warehouseId = null);

    /// <summary>
    /// Searches purchase orders with filters, sorting, and pagination
    /// </summary>
    Task<(IEnumerable<PurchaseOrder>, int)> SearchAsync(
        string? status = null,
        long? supplierId = null,
        long? warehouseId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int pageNumber = 1,
        int pageSize = 10,
        string sortBy = "order_date",
        string sortOrder = "desc");

    /// <summary>
    /// Gets purchase orders by supplier ID
    /// </summary>
    Task<IEnumerable<PurchaseOrder>> GetBySupplierIdAsync(long supplierId);

    /// <summary>
    /// Gets purchase orders by warehouse ID
    /// </summary>
    Task<IEnumerable<PurchaseOrder>> GetByWarehouseIdAsync(long warehouseId);

    /// <summary>
    /// Gets purchase orders by status
    /// </summary>
    Task<IEnumerable<PurchaseOrder>> GetByStatusAsync(string status);

    /// <summary>
    /// Creates a new purchase order
    /// </summary>
    Task<PurchaseOrder> CreateAsync(PurchaseOrder purchaseOrder);

    /// <summary>
    /// Updates an existing purchase order
    /// </summary>
    Task<PurchaseOrder> UpdateAsync(PurchaseOrder purchaseOrder);

    /// <summary>
    /// Updates purchase order status
    /// </summary>
    Task<bool> UpdateStatusAsync(long id, string status);

    /// <summary>
    /// Deletes a purchase order (only if in draft/pending status)
    /// </summary>
    Task<bool> DeleteAsync(long id);

    /// <summary>
    /// Checks if a purchase order can be deleted
    /// </summary>
    Task<bool> CanDeleteAsync(long id);

    /// <summary>
    /// Gets purchase orders awaiting approval (pending status)
    /// </summary>
    Task<IEnumerable<PurchaseOrder>> GetPendingApprovalsAsync();

    /// <summary>
    /// Gets total amount for purchase orders by status and date range
    /// </summary>
    Task<decimal> GetTotalAmountAsync(string? status = null, DateTime? startDate = null, DateTime? endDate = null);
}
