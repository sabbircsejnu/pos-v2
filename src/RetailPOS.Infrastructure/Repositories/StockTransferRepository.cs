using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Stock Transfer data access operations
/// </summary>
public class StockTransferRepository : IStockTransferRepository
{
    private readonly RetailPOSDbContext _context;

    public StockTransferRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<StockTransfer?> GetByIdAsync(long id)
    {
        return await _context.StockTransfers
            .Include(st => st.Approver)
            .Include(st => st.Creator)
            .Include(st => st.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(st => st.Id == id);
    }

    public async Task<IEnumerable<StockTransfer>> GetAllAsync(string? status = null, long? fromLocationId = null, long? toLocationId = null)
    {
        var query = _context.StockTransfers
            .Include(st => st.Approver)
            .Include(st => st.Creator)
            .Include(st => st.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var lowerStatus = status.ToLower();
            query = query.Where(st => st.Status.ToLower() == lowerStatus);
        }

        if (fromLocationId.HasValue)
            query = query.Where(st => st.FromLocationId == fromLocationId.Value);

        if (toLocationId.HasValue)
            query = query.Where(st => st.ToLocationId == toLocationId.Value);

        return await query.OrderByDescending(st => st.TransferDate).ToListAsync();
    }

    public async Task<(IEnumerable<StockTransfer>, int)> SearchAsync(
        string? status,
        long? fromLocationId,
        string? fromLocationType,
        long? toLocationId,
        string? toLocationType,
        int pageNumber,
        int pageSize)
    {
        var query = _context.StockTransfers
            .Include(st => st.Approver)
            .Include(st => st.Creator)
            .Include(st => st.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v.Product)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var lowerStatus = status.ToLower();
            query = query.Where(st => st.Status.ToLower() == lowerStatus);
        }

        if (fromLocationId.HasValue)
            query = query.Where(st => st.FromLocationId == fromLocationId.Value);

        if (!string.IsNullOrWhiteSpace(fromLocationType))
            query = query.Where(st => st.FromLocationType.ToLower() == fromLocationType.ToLower());

        if (toLocationId.HasValue)
            query = query.Where(st => st.ToLocationId == toLocationId.Value);

        if (!string.IsNullOrWhiteSpace(toLocationType))
            query = query.Where(st => st.ToLocationType.ToLower() == toLocationType.ToLower());

        var totalCount = await query.CountAsync();

        var transfers = await query
            .OrderByDescending(st => st.TransferDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (transfers, totalCount);
    }

    public async Task<StockTransfer> CreateAsync(StockTransfer transfer)
    {
        _context.StockTransfers.Add(transfer);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(transfer.Id))!;
    }

    public async Task<StockTransfer> UpdateAsync(StockTransfer transfer)
    {
        _context.StockTransfers.Update(transfer);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(transfer.Id))!;
    }

    public async Task<bool> UpdateStatusAsync(long id, string status, long? approvedBy = null)
    {
        var transfer = await _context.StockTransfers.FindAsync(id);
        if (transfer == null)
            return false;

        transfer.Status = status;

        if (approvedBy.HasValue)
            transfer.ApprovedBy = approvedBy;

        await _context.SaveChangesAsync();
        return true;
    }
}
