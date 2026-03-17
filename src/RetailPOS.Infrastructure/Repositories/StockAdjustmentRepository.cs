using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Stock Adjustment data access operations
/// </summary>
public class StockAdjustmentRepository : IStockAdjustmentRepository
{
    private readonly RetailPOSDbContext _context;

    public StockAdjustmentRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<StockAdjustment?> GetByIdAsync(long id)
    {
        return await _context.StockAdjustments
            .Include(sa => sa.Variant)
                .ThenInclude(v => v.Product)
            .Include(sa => sa.Adjuster)
            .FirstOrDefaultAsync(sa => sa.Id == id);
    }

    public async Task<IEnumerable<StockAdjustment>> GetAllAsync(long? locationId = null, string? locationType = null, long? variantId = null)
    {
        var query = _context.StockAdjustments
            .Include(sa => sa.Variant)
                .ThenInclude(v => v.Product)
            .Include(sa => sa.Adjuster)
            .AsQueryable();

        if (locationId.HasValue)
            query = query.Where(sa => sa.LocationId == locationId.Value);

        if (!string.IsNullOrWhiteSpace(locationType))
            query = query.Where(sa => sa.LocationType.ToLower() == locationType.ToLower());

        if (variantId.HasValue)
            query = query.Where(sa => sa.VariantId == variantId.Value);

        return await query.OrderByDescending(sa => sa.AdjustmentDate).ToListAsync();
    }

    public async Task<(IEnumerable<StockAdjustment>, int)> SearchAsync(
        long? locationId,
        string? locationType,
        long? variantId,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize)
    {
        var query = _context.StockAdjustments
            .Include(sa => sa.Variant)
                .ThenInclude(v => v.Product)
            .Include(sa => sa.Adjuster)
            .AsQueryable();

        if (locationId.HasValue)
            query = query.Where(sa => sa.LocationId == locationId.Value);

        if (!string.IsNullOrWhiteSpace(locationType))
            query = query.Where(sa => sa.LocationType.ToLower() == locationType.ToLower());

        if (variantId.HasValue)
            query = query.Where(sa => sa.VariantId == variantId.Value);

        if (startDate.HasValue)
            query = query.Where(sa => sa.AdjustmentDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(sa => sa.AdjustmentDate <= endDate.Value);

        var totalCount = await query.CountAsync();

        var adjustments = await query
            .OrderByDescending(sa => sa.AdjustmentDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (adjustments, totalCount);
    }

    public async Task<IEnumerable<StockAdjustment>> GetHistoryAsync(long variantId, long locationId)
    {
        return await _context.StockAdjustments
            .Include(sa => sa.Variant)
                .ThenInclude(v => v.Product)
            .Include(sa => sa.Adjuster)
            .Where(sa => sa.VariantId == variantId && sa.LocationId == locationId)
            .OrderByDescending(sa => sa.AdjustmentDate)
            .ToListAsync();
    }

    public async Task<StockAdjustment> CreateAsync(StockAdjustment adjustment)
    {
        _context.StockAdjustments.Add(adjustment);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(adjustment.Id))!;
    }
}
