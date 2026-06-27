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
            .Include(sa => sa.Lines)
                .ThenInclude(l => l.Variant)
                    .ThenInclude(v => v.Product)
            .Include(sa => sa.Adjuster)
            .Include(sa => sa.Approver)
            .Include(sa => sa.Rejector)
            .Include(sa => sa.Canceller)
            .FirstOrDefaultAsync(sa => sa.Id == id);
    }

    public async Task<IEnumerable<StockAdjustment>> GetAllAsync(long? locationId = null, string? locationType = null, long? variantId = null, string? status = null)
    {
        var query = _context.StockAdjustments
            .Include(sa => sa.Lines)
                .ThenInclude(l => l.Variant)
                    .ThenInclude(v => v.Product)
            .Include(sa => sa.Adjuster)
            .Include(sa => sa.Approver)
            .Include(sa => sa.Rejector)
            .Include(sa => sa.Canceller)
            .AsQueryable();

        if (locationId.HasValue)
            query = query.Where(sa => sa.LocationId == locationId.Value);

        if (!string.IsNullOrWhiteSpace(locationType))
            query = query.Where(sa => sa.LocationType.ToLower() == locationType.ToLower());

        if (variantId.HasValue)
            query = query.Where(sa => sa.Lines.Any(line => line.VariantId == variantId.Value));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(sa => sa.Status == status);

        return await query.OrderByDescending(sa => sa.AdjustmentDate).ToListAsync();
    }

    public async Task<(IEnumerable<StockAdjustment>, int)> SearchAsync(
        long? locationId,
        string? locationType,
        long? variantId,
        string? status,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize)
    {
        var query = _context.StockAdjustments
            .Include(sa => sa.Lines)
                .ThenInclude(l => l.Variant)
                    .ThenInclude(v => v.Product)
            .Include(sa => sa.Adjuster)
            .Include(sa => sa.Approver)
            .Include(sa => sa.Rejector)
            .Include(sa => sa.Canceller)
            .AsQueryable();

        if (locationId.HasValue)
            query = query.Where(sa => sa.LocationId == locationId.Value);

        if (!string.IsNullOrWhiteSpace(locationType))
            query = query.Where(sa => sa.LocationType.ToLower() == locationType.ToLower());

        if (variantId.HasValue)
            query = query.Where(sa => sa.Lines.Any(line => line.VariantId == variantId.Value));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(sa => sa.Status == status);

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
            .Include(sa => sa.Lines)
                .ThenInclude(l => l.Variant)
                    .ThenInclude(v => v.Product)
            .Include(sa => sa.Adjuster)
            .Include(sa => sa.Approver)
            .Include(sa => sa.Rejector)
            .Include(sa => sa.Canceller)
            .Where(sa => sa.LocationId == locationId && sa.Lines.Any(line => line.VariantId == variantId))
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
