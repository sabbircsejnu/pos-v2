using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class StockRequisitionRepository : IStockRequisitionRepository
{
    private readonly RetailPOSDbContext _context;

    public StockRequisitionRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<StockRequisition?> GetByIdAsync(long id)
    {
        return await _context.StockRequisitions
            .Include(r => r.Requester)
            .Include(r => r.Approver)
            .Include(r => r.Submitter)
            .Include(r => r.Rejector)
            .Include(r => r.Lines)
                .ThenInclude(l => l.Variant)
                    .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<IEnumerable<StockRequisition>> GetAllAsync(string? status = null, long? requestingLocationId = null, long? sourceLocationId = null)
    {
        var query = _context.StockRequisitions
            .Include(r => r.Requester)
            .Include(r => r.Approver)
            .Include(r => r.Submitter)
            .Include(r => r.Rejector)
            .Include(r => r.Lines)
                .ThenInclude(l => l.Variant)
                    .ThenInclude(v => v.Product)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var lowerStatus = status.ToLowerInvariant();
            query = query.Where(r => r.Status.ToLower() == lowerStatus);
        }

        if (requestingLocationId.HasValue)
            query = query.Where(r => r.RequestingLocationId == requestingLocationId.Value);

        if (sourceLocationId.HasValue)
            query = query.Where(r => r.SourceLocationId == sourceLocationId.Value);

        return await query.OrderByDescending(r => r.RequestDate).ToListAsync();
    }

    public async Task<(IEnumerable<StockRequisition>, int)> SearchAsync(
        string? status,
        long? requestingLocationId,
        string? requestingLocationType,
        long? sourceLocationId,
        string? sourceLocationType,
        int pageNumber,
        int pageSize)
    {
        var query = _context.StockRequisitions
            .Include(r => r.Requester)
            .Include(r => r.Approver)
            .Include(r => r.Submitter)
            .Include(r => r.Rejector)
            .Include(r => r.Lines)
                .ThenInclude(l => l.Variant)
                    .ThenInclude(v => v.Product)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var lowerStatus = status.ToLowerInvariant();
            query = query.Where(r => r.Status.ToLower() == lowerStatus);
        }

        if (requestingLocationId.HasValue)
            query = query.Where(r => r.RequestingLocationId == requestingLocationId.Value);

        if (!string.IsNullOrWhiteSpace(requestingLocationType))
        {
            var lowerType = requestingLocationType.ToLowerInvariant();
            query = query.Where(r => r.RequestingLocationType.ToLower() == lowerType);
        }

        if (sourceLocationId.HasValue)
            query = query.Where(r => r.SourceLocationId == sourceLocationId.Value);

        if (!string.IsNullOrWhiteSpace(sourceLocationType))
        {
            var lowerType = sourceLocationType.ToLowerInvariant();
            query = query.Where(r => r.SourceLocationType.ToLower() == lowerType);
        }

        var totalCount = await query.CountAsync();
        var requisitions = await query
            .OrderByDescending(r => r.RequestDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (requisitions, totalCount);
    }

    public async Task<StockRequisition> CreateAsync(StockRequisition requisition)
    {
        _context.StockRequisitions.Add(requisition);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(requisition.Id))!;
    }

    public async Task<StockRequisition> UpdateAsync(StockRequisition requisition)
    {
        _context.StockRequisitions.Update(requisition);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(requisition.Id))!;
    }
}
