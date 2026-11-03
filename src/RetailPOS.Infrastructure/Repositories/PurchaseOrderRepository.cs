using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.PurchaseOrders;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Purchase Order data access operations
/// </summary>
public class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private readonly RetailPOSDbContext _context;

    public PurchaseOrderRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<PurchaseOrder?> GetByIdAsync(long id, long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Outlet)
            .Include(po => po.Creator)
            .Include(po => po.Grns)
            .Include(po => po.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v.Product)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        return await query.FirstOrDefaultAsync(po => po.Id == id);
    }

    public async Task<IEnumerable<PurchaseOrder>> GetAllAsync(string? status = null, long? supplierId = null, long? warehouseId = null, long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Outlet)
            .Include(po => po.Creator)
            .Include(po => po.Items)
            .AsQueryable();

        if (businessId.HasValue)
        {
            query = query.WhereBusiness(businessId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var lowerStatus = status.ToLower();
            query = query.Where(po => po.Status.ToLower() == lowerStatus);
        }

        if (supplierId.HasValue)
        {
            query = query.Where(po => po.SupplierId == supplierId.Value);
        }

        if (warehouseId.HasValue)
        {
            query = query.Where(po => po.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderByDescending(po => po.OrderDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<PurchaseOrder>> GetBySupplierIdAsync(long supplierId, long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Outlet)
            .Include(po => po.Items)
            .Where(po => po.SupplierId == supplierId)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        return await query
            .OrderByDescending(po => po.OrderDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<PurchaseOrder>> GetByWarehouseIdAsync(long warehouseId, long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Outlet)
            .Include(po => po.Items)
            .Where(po => po.WarehouseId == warehouseId)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        return await query
            .OrderByDescending(po => po.OrderDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<PurchaseOrder>> GetByStatusAsync(string status, long? businessId = null)
    {
        var lowerStatus = status.ToLower();
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Outlet)
            .Include(po => po.Items)
            .Where(po => po.Status.ToLower() == lowerStatus)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        return await query
            .OrderByDescending(po => po.OrderDate)
            .ToListAsync();
    }

    public async Task<PurchaseOrder> UpdateAsync(PurchaseOrder purchaseOrder)
    {
        purchaseOrder.UpdatedAt = DateTime.UtcNow;

        _context.PurchaseOrders.Update(purchaseOrder);
        await _context.SaveChangesAsync();
        
        // Reload with navigation properties
        return (await GetByIdAsync(purchaseOrder.Id, purchaseOrder.LocationBusinessId))!;
    }

    public async Task<bool> UpdateStatusAsync(long id, string status, long? businessId = null)
    {
        var query = _context.PurchaseOrders.AsQueryable();
        if (businessId.HasValue)
            query = query.WhereBusiness(businessId.Value);

        var purchaseOrder = await query.FirstOrDefaultAsync(po => po.Id == id);
        if (purchaseOrder == null)
            return false;

        purchaseOrder.Status = status;
        purchaseOrder.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

}
