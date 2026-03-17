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

    public async Task<PurchaseOrder?> GetByIdAsync(long id)
    {
        return await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Creator)
            .Include(po => po.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(po => po.Id == id);
    }

    public async Task<IEnumerable<PurchaseOrder>> GetAllAsync(string? status = null, long? supplierId = null, long? warehouseId = null)
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Creator)
            .Include(po => po.Items)
            .AsQueryable();

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
        string sortOrder = "desc")
    {
        var query = _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Creator)
            .Include(po => po.Items)
            .AsQueryable();

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

    public async Task<IEnumerable<PurchaseOrder>> GetBySupplierIdAsync(long supplierId)
    {
        return await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Items)
            .Where(po => po.SupplierId == supplierId)
            .OrderByDescending(po => po.OrderDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<PurchaseOrder>> GetByWarehouseIdAsync(long warehouseId)
    {
        return await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Items)
            .Where(po => po.WarehouseId == warehouseId)
            .OrderByDescending(po => po.OrderDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<PurchaseOrder>> GetByStatusAsync(string status)
    {
        var lowerStatus = status.ToLower();
        return await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Items)
            .Where(po => po.Status.ToLower() == lowerStatus)
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
        return (await GetByIdAsync(purchaseOrder.Id))!;
    }

    public async Task<PurchaseOrder> UpdateAsync(PurchaseOrder purchaseOrder)
    {
        purchaseOrder.UpdatedAt = DateTime.UtcNow;

        _context.PurchaseOrders.Update(purchaseOrder);
        await _context.SaveChangesAsync();
        
        // Reload with navigation properties
        return (await GetByIdAsync(purchaseOrder.Id))!;
    }

    public async Task<bool> UpdateStatusAsync(long id, string status)
    {
        var purchaseOrder = await _context.PurchaseOrders.FindAsync(id);
        if (purchaseOrder == null)
            return false;

        purchaseOrder.Status = status;
        purchaseOrder.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var purchaseOrder = await _context.PurchaseOrders
            .Include(po => po.Items)
            .FirstOrDefaultAsync(po => po.Id == id);

        if (purchaseOrder == null)
            return false;

        // Remove items first
        _context.PurchaseOrderItems.RemoveRange(purchaseOrder.Items);
        _context.PurchaseOrders.Remove(purchaseOrder);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CanDeleteAsync(long id)
    {
        var purchaseOrder = await _context.PurchaseOrders
            .Include(po => po.Grns)
            .FirstOrDefaultAsync(po => po.Id == id);

        if (purchaseOrder == null)
            return false;

        // Can only delete if status is draft or pending AND no GRNs created
        var allowedStatuses = new[] { "draft", "pending" };
        return allowedStatuses.Contains(purchaseOrder.Status.ToLower()) && !purchaseOrder.Grns.Any();
    }

    public async Task<IEnumerable<PurchaseOrder>> GetPendingApprovalsAsync()
    {
        return await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Creator)
            .Include(po => po.Items)
            .Where(po => po.Status.ToLower() == "pending")
            .OrderBy(po => po.OrderDate)
            .ToListAsync();
    }

    public async Task<decimal> GetTotalAmountAsync(string? status = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _context.PurchaseOrders.AsQueryable();

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
