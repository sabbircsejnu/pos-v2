// =====================================================================
// NEW — Stock Ledger repository implementation
// =====================================================================
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class StockLedgerRepository : IStockLedgerRepository
{
    private readonly RetailPOSDbContext _context;

    public StockLedgerRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<StockLedger?> GetByIdAsync(long id)
    {
        return await _context.StockLedgers
            .Include(sl => sl.Variant)
                .ThenInclude(v => v.Product)
            .Include(sl => sl.Creator)
            .FirstOrDefaultAsync(sl => sl.Id == id);
    }

    public async Task<List<StockLedger>> GetByVariantAndLocationAsync(
        long variantId, long locationId, string locationType)
    {
        return await _context.StockLedgers
            .Include(sl => sl.Variant)
                .ThenInclude(v => v.Product)
            .Include(sl => sl.Creator)
            .Where(sl =>
                sl.VariantId == variantId &&
                sl.LocationId == locationId &&
                sl.LocationType.ToLower() == locationType.ToLower())
            .OrderBy(sl => sl.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<StockLedger>> GetByReferenceAsync(string referenceType, long referenceId)
    {
        return await _context.StockLedgers
            .Include(sl => sl.Variant)
                .ThenInclude(v => v.Product)
            .Include(sl => sl.Creator)
            .Where(sl =>
                sl.ReferenceType.ToLower() == referenceType.ToLower() &&
                sl.ReferenceId == referenceId)
            .OrderBy(sl => sl.CreatedAt)
            .ToListAsync();
    }

    public async Task<(List<StockLedger> Entries, int TotalCount)> SearchAsync(
        long? variantId,
        long? locationId,
        string? locationType,
        string? transactionType,
        string? referenceType,
        long? referenceId,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize)
    {
        var query = _context.StockLedgers
            .Include(sl => sl.Variant)
                .ThenInclude(v => v.Product)
            .Include(sl => sl.Creator)
            .AsQueryable();

        if (variantId.HasValue)
            query = query.Where(sl => sl.VariantId == variantId.Value);

        if (locationId.HasValue)
            query = query.Where(sl => sl.LocationId == locationId.Value);

        if (!string.IsNullOrWhiteSpace(locationType))
            query = query.Where(sl => sl.LocationType.ToLower() == locationType.ToLower());

        if (!string.IsNullOrWhiteSpace(transactionType))
            query = query.Where(sl => sl.TransactionType.ToLower() == transactionType.ToLower());

        if (!string.IsNullOrWhiteSpace(referenceType))
            query = query.Where(sl => sl.ReferenceType.ToLower() == referenceType.ToLower());

        if (referenceId.HasValue)
            query = query.Where(sl => sl.ReferenceId == referenceId.Value);

        if (startDate.HasValue)
            query = query.Where(sl => sl.CreatedAt >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(sl => sl.CreatedAt <= endDate.Value);

        var totalCount = await query.CountAsync();

        var entries = await query
            .OrderByDescending(sl => sl.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (entries, totalCount);
    }
}
