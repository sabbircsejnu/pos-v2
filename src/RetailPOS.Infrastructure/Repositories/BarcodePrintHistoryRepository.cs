using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class BarcodePrintHistoryRepository : IBarcodePrintHistoryRepository
{
    private readonly RetailPOSDbContext _context;

    public BarcodePrintHistoryRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<BarcodePrintHistory> CreateAsync(BarcodePrintHistory history)
    {
        _context.Set<BarcodePrintHistory>().Add(history);
        await _context.SaveChangesAsync();
        return history;
    }

    public async Task<BarcodePrintHistory?> GetByIdAsync(long id)
    {
        return await _context.Set<BarcodePrintHistory>()
            .Include(h => h.Template)
            .Include(h => h.Outlet)
            .Include(h => h.PrintedByUser)
            .Include(h => h.Items.OrderBy(i => i.Id))
            .FirstOrDefaultAsync(h => h.Id == id);
    }

    public async Task<(List<BarcodePrintHistory> Rows, int TotalCount)> SearchAsync(
        long businessId,
        long? templateId,
        long? printedByUserId,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize)
    {
        var query = _context.Set<BarcodePrintHistory>()
            .Include(h => h.Template)
            .Include(h => h.Outlet)
            .Include(h => h.PrintedByUser)
            .Where(h => h.BusinessId == businessId)
            .AsQueryable();

        if (templateId.HasValue)
        {
            query = query.Where(h => h.TemplateId == templateId.Value);
        }

        if (printedByUserId.HasValue)
        {
            query = query.Where(h => h.PrintedByUserId == printedByUserId.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(h => h.PrintedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(h => h.PrintedAt <= endDate.Value);
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(h => h.PrintedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (rows, total);
    }
}
