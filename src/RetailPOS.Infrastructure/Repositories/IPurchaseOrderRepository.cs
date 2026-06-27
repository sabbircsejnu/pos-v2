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
    Task<PurchaseOrder?> GetByIdAsync(long id, long? businessId = null);

    /// <summary>
    /// Gets all purchase orders with optional filters
    /// </summary>
    Task<IEnumerable<PurchaseOrder>> GetAllAsync(string? status = null, long? supplierId = null, long? warehouseId = null, long? businessId = null);

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
        string sortOrder = "desc",
        long? businessId = null);

    /// <summary>
    /// Gets purchase orders by supplier ID
    /// </summary>
    Task<IEnumerable<PurchaseOrder>> GetBySupplierIdAsync(long supplierId, long? businessId = null);

    /// <summary>
    /// Gets purchase orders by warehouse ID
    /// </summary>
    Task<IEnumerable<PurchaseOrder>> GetByWarehouseIdAsync(long warehouseId, long? businessId = null);

    /// <summary>
    /// Gets purchase orders by status
    /// </summary>
    Task<IEnumerable<PurchaseOrder>> GetByStatusAsync(string status, long? businessId = null);

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
    Task<bool> UpdateStatusAsync(long id, string status, long? businessId = null);

    /// <summary>
    /// Deletes a purchase order (only if in draft/pending status)
    /// </summary>
    Task<bool> DeleteAsync(long id, long? businessId = null);

    /// <summary>
    /// Checks if a purchase order can be deleted
    /// </summary>
    Task<bool> CanDeleteAsync(long id, long? businessId = null);

    /// <summary>
    /// Gets purchase orders awaiting approval (pending status)
    /// </summary>
    Task<IEnumerable<PurchaseOrder>> GetPendingApprovalsAsync(long? businessId = null);

    /// <summary>
    /// Sets the formatted PO number after initial creation.
    /// </summary>
    Task SetPoNumberAsync(long id, string poNumber);

    /// <summary>
    /// Gets total amount for purchase orders by status and date range
    /// </summary>
    Task<decimal> GetTotalAmountAsync(string? status = null, DateTime? startDate = null, DateTime? endDate = null, long? businessId = null);
}
