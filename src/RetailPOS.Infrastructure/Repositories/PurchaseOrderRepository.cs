using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

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
            .Include(po => po.Creator)
            .Include(po => po.Grns)
            .Include(po => po.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v.Product)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);

        return await query.FirstOrDefaultAsync(po => po.Id == id);
    }

    public async Task<IEnumerable<PurchaseOrder>> GetAllAsync(string? status = null, long? supplierId = null, long? warehouseId = null, long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Creator)
            .Include(po => po.Items)
            .AsQueryable();

        if (businessId.HasValue)
        {
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);
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

    public async Task<(IEnumerable<PurchaseOrder>, int)> SearchAsync(
        string? status = null,
        long? supplierId = null,
        long? warehouseId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int pageNumber = 1,
        int pageSize = 10,
        string sortBy = "order_date",
        string sortOrder = "desc",
        long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Creator)
            .Include(po => po.Items)
            .AsQueryable();

        if (businessId.HasValue)
        {
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);
        }

        // Apply filters
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

        if (startDate.HasValue)
        {
            query = query.Where(po => po.OrderDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(po => po.OrderDate <= endDate.Value);
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = sortBy.ToLower() switch
        {
            "total_amount" => sortOrder.ToLower() == "asc"
                ? query.OrderBy(po => po.TotalAmount)
                : query.OrderByDescending(po => po.TotalAmount),
            "supplier" => sortOrder.ToLower() == "asc"
                ? query.OrderBy(po => po.Supplier.Name)
                : query.OrderByDescending(po => po.Supplier.Name),
            "status" => sortOrder.ToLower() == "asc"
                ? query.OrderBy(po => po.Status)
                : query.OrderByDescending(po => po.Status),
            _ => sortOrder.ToLower() == "asc"
                ? query.OrderBy(po => po.OrderDate)
                : query.OrderByDescending(po => po.OrderDate),
        };

        // Apply pagination
        var purchaseOrders = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (purchaseOrders, totalCount);
    }

    public async Task<IEnumerable<PurchaseOrder>> GetBySupplierIdAsync(long supplierId, long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Items)
            .Where(po => po.SupplierId == supplierId)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);

        return await query
            .OrderByDescending(po => po.OrderDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<PurchaseOrder>> GetByWarehouseIdAsync(long warehouseId, long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Items)
            .Where(po => po.WarehouseId == warehouseId)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);

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
            .Include(po => po.Items)
            .Where(po => po.Status.ToLower() == lowerStatus)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);

        return await query
            .OrderByDescending(po => po.OrderDate)
            .ToListAsync();
    }

    public async Task<PurchaseOrder> CreateAsync(PurchaseOrder purchaseOrder)
    {
        purchaseOrder.CreatedAt = DateTime.UtcNow;
        purchaseOrder.UpdatedAt = DateTime.UtcNow;

        _context.PurchaseOrders.Add(purchaseOrder);
        await _context.SaveChangesAsync();
        
        // Reload with navigation properties
        return (await GetByIdAsync(purchaseOrder.Id, purchaseOrder.Warehouse?.BusinessId))!;
    }

    public async Task<PurchaseOrder> UpdateAsync(PurchaseOrder purchaseOrder)
    {
        purchaseOrder.UpdatedAt = DateTime.UtcNow;

        _context.PurchaseOrders.Update(purchaseOrder);
        await _context.SaveChangesAsync();
        
        // Reload with navigation properties
        return (await GetByIdAsync(purchaseOrder.Id, purchaseOrder.Warehouse?.BusinessId))!;
    }

    public async Task<bool> UpdateStatusAsync(long id, string status, long? businessId = null)
    {
        var query = _context.PurchaseOrders.AsQueryable();
        if (businessId.HasValue)
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);

        var purchaseOrder = await query.FirstOrDefaultAsync(po => po.Id == id);
        if (purchaseOrder == null)
            return false;

        purchaseOrder.Status = status;
        purchaseOrder.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(long id, long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Items)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);

        var purchaseOrder = await query.FirstOrDefaultAsync(po => po.Id == id);

        if (purchaseOrder == null)
            return false;

        // Remove items first
        _context.PurchaseOrderItems.RemoveRange(purchaseOrder.Items);
        _context.PurchaseOrders.Remove(purchaseOrder);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CanDeleteAsync(long id, long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Grns)
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);

        var purchaseOrder = await query.FirstOrDefaultAsync(po => po.Id == id);

        if (purchaseOrder == null)
            return false;

        // Can only delete if status is draft or pending AND no GRNs created
        var allowedStatuses = new[] { "draft", "pending" };
        return allowedStatuses.Contains(purchaseOrder.Status.ToLower()) && !purchaseOrder.Grns.Any();
    }

    public async Task<IEnumerable<PurchaseOrder>> GetPendingApprovalsAsync(long? businessId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Creator)
            .Include(po => po.Items)
            .Where(po => po.Status.ToLower() == "pending")
            .AsQueryable();

        if (businessId.HasValue)
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);

        return await query
            .OrderBy(po => po.OrderDate)
            .ToListAsync();
    }

    public async Task SetPoNumberAsync(long id, string poNumber)
    {
        var po = await _context.PurchaseOrders.FindAsync(id);
        if (po == null) return;
        po.PoNumber = poNumber;
        await _context.SaveChangesAsync();
    }

    public async Task<decimal> GetTotalAmountAsync(string? status = null, DateTime? startDate = null, DateTime? endDate = null, long? businessId = null)
    {
        var query = _context.PurchaseOrders.AsQueryable();

        if (businessId.HasValue)
        {
            query = query.Where(po => po.Warehouse.BusinessId == businessId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var lowerStatus = status.ToLower();
            query = query.Where(po => po.Status.ToLower() == lowerStatus);
        }

        if (startDate.HasValue)
        {
            query = query.Where(po => po.OrderDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(po => po.OrderDate <= endDate.Value);
        }

        return await query.SumAsync(po => po.TotalAmount);
    }
}
