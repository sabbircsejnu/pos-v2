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
    /// Updates an existing purchase order
    /// </summary>
    Task<PurchaseOrder> UpdateAsync(PurchaseOrder purchaseOrder);

    /// <summary>
    /// Updates purchase order status
    /// </summary>
    Task<bool> UpdateStatusAsync(long id, string status, long? businessId = null);

}
