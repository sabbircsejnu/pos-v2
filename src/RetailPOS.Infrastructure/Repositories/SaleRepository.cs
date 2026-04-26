using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Sale data access operations
/// </summary>
public class SaleRepository : ISaleRepository
{
    private readonly RetailPOSDbContext _context;

    public SaleRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Sale?> GetByIdAsync(long id)
    {
        return await _context.Sales
            .Include(s => s.Outlet)
            .Include(s => s.Customer)
            .Include(s => s.Cashier)
            .Include(s => s.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v.Product)
            .Include(s => s.Payments)          // UPDATED — load split payments
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<(IEnumerable<Sale>, int)> SearchAsync(
        long? outletId, long? cashierId, long? customerId,
        DateTime? startDate, DateTime? endDate, string? status,
        int pageNumber, int pageSize)
    {
        var query = _context.Sales
            .Include(s => s.Outlet)
            .Include(s => s.Customer)
            .Include(s => s.Cashier)
            .Include(s => s.Items)
            .AsQueryable();

        if (outletId.HasValue)
            query = query.Where(s => s.OutletId == outletId.Value);

        if (cashierId.HasValue)
            query = query.Where(s => s.CashierId == cashierId.Value);

        if (customerId.HasValue)
            query = query.Where(s => s.CustomerId == customerId.Value);

        if (startDate.HasValue)
            query = query.Where(s => s.SaleDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(s => s.SaleDate <= endDate.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var lower = status.ToLower();
            query = query.Where(s => s.Status.ToLower() == lower);
        }

        var totalCount = await query.CountAsync();

        var sales = await query
            .OrderByDescending(s => s.SaleDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (sales, totalCount);
    }

    public async Task<IEnumerable<Sale>> GetTodaysSalesAsync(long? outletId = null)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var query = _context.Sales
            .Include(s => s.Items)
            .Where(s => s.SaleDate >= today && s.SaleDate < tomorrow && s.Status != "voided")
            .AsQueryable();

        if (outletId.HasValue)
            query = query.Where(s => s.OutletId == outletId.Value);

        return await query.ToListAsync();
    }

    public async Task<Sale> CreateAsync(Sale sale)
    {
        sale.CreatedAt = DateTime.UtcNow;
        _context.Sales.Add(sale);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(sale.Id))!;
    }

    public async Task<Sale> UpdateAsync(Sale sale)
    {
        _context.Sales.Update(sale);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(sale.Id))!;
    }

    // UPDATED — idempotency lookup
    public async Task<Sale?> FindByIdempotencyKeyAsync(long outletId, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return null;
        return await _context.Sales
            .Include(s => s.Outlet)
            .Include(s => s.Customer)
            .Include(s => s.Cashier)
            .Include(s => s.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v.Product)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s =>
                s.OutletId == outletId &&
                s.IdempotencyKey == idempotencyKey);
    }
}
