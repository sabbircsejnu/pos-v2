using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for GRN (Goods Received Note) data access operations
/// </summary>
public class GrnRepository : IGrnRepository
{
    private readonly RetailPOSDbContext _context;

    public GrnRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Grn?> GetByIdAsync(long id)
    {
        return await _context.Grns
            .Include(g => g.PurchaseOrder)
                .ThenInclude(po => po.Supplier)
            .Include(g => g.PurchaseOrder)
                .ThenInclude(po => po.Warehouse)
            .Include(g => g.Creator)
            .Include(g => g.Items)
                .ThenInclude(i => i.PurchaseOrderItem)
                    .ThenInclude(poi => poi.Variant)
                        .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<List<Grn>> GetAllAsync(long? poId, string? status)
    {
        var query = _context.Grns
            .Include(g => g.PurchaseOrder)
                .ThenInclude(po => po.Supplier)
            .Include(g => g.PurchaseOrder)
                .ThenInclude(po => po.Warehouse)
            .Include(g => g.Creator)
            .Include(g => g.Items)
            .AsQueryable();

        if (poId.HasValue)
            query = query.Where(g => g.PoId == poId.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var lowerStatus = status.ToLower();
            query = query.Where(g => g.Status.ToLower() == lowerStatus);
        }

        return await query
            .OrderByDescending(g => g.ReceivedDate)
            .ToListAsync();
    }

    public async Task<(List<Grn> Items, int TotalCount)> SearchAsync(
        long? poId,
        string? status,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize)
    {
        var query = _context.Grns
            .Include(g => g.PurchaseOrder)
                .ThenInclude(po => po.Supplier)
            .Include(g => g.PurchaseOrder)
                .ThenInclude(po => po.Warehouse)
            .Include(g => g.Creator)
            .Include(g => g.Items)
            .AsQueryable();

        if (poId.HasValue)
            query = query.Where(g => g.PoId == poId.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var lowerStatus = status.ToLower();
            query = query.Where(g => g.Status.ToLower() == lowerStatus);
        }

        if (startDate.HasValue)
            query = query.Where(g => g.ReceivedDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(g => g.ReceivedDate <= endDate.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(g => g.ReceivedDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Grn> CreateAsync(Grn entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        _context.Grns.Add(entity);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(entity.Id))!;
    }

    public async Task<Grn> UpdateAsync(Grn entity)
    {
        _context.Grns.Update(entity);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(entity.Id))!;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var grn = await _context.Grns
            .Include(g => g.Items)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (grn == null)
            return false;

        _context.GrnItems.RemoveRange(grn.Items);
        _context.Grns.Remove(grn);
        await _context.SaveChangesAsync();
        return true;
    }

    // UPDATED: also returns "partial" POs so a second GRN can be raised on partially received POs
    public async Task<List<PurchaseOrder>> GetPendingReceiptPOsAsync()
    {
        return await _context.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Warehouse)
            .Include(po => po.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.Images.Where(img => img.IsPrimary))
            .Where(po => po.Status.ToLower() == "approved" || po.Status.ToLower() == "partial")
            .OrderBy(po => po.OrderDate)
            .ToListAsync();
    }

    // NEW: all GRNs for a PO with full item navigation (for cumulative variance report)
    public async Task<List<Grn>> GetPoGrnsWithItemsAsync(long poId)
    {
        return await _context.Grns
            .Include(g => g.Items)
                .ThenInclude(i => i.PurchaseOrderItem)
                    .ThenInclude(poi => poi.Variant)
                        .ThenInclude(v => v.Product)
            .Where(g => g.PoId == poId)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync();
    }
}
