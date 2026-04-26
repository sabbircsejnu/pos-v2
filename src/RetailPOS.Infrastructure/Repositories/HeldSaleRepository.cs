// NEW
using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class HeldSaleRepository : IHeldSaleRepository
{
    private readonly RetailPOSDbContext _context;

    public HeldSaleRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<HeldSale?> GetByIdAsync(long id)
    {
        return await _context.HeldSales
            .Include(h => h.Outlet)
            .Include(h => h.Cashier)
            .Include(h => h.Customer)
            .FirstOrDefaultAsync(h => h.Id == id);
    }

    public async Task<IEnumerable<HeldSale>> GetByOutletAsync(long outletId)
    {
        return await _context.HeldSales
            .Include(h => h.Customer)
            .Include(h => h.Cashier)
            .Where(h => h.OutletId == outletId)
            .OrderByDescending(h => h.HeldAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<HeldSale>> GetByCashierAsync(long cashierId)
    {
        return await _context.HeldSales
            .Include(h => h.Customer)
            .Include(h => h.Outlet)
            .Where(h => h.CashierId == cashierId)
            .OrderByDescending(h => h.HeldAt)
            .ToListAsync();
    }

    public async Task<HeldSale> CreateAsync(HeldSale heldSale)
    {
        heldSale.HeldAt = DateTime.UtcNow;
        _context.HeldSales.Add(heldSale);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(heldSale.Id))!;
    }

    public async Task DeleteAsync(long id)
    {
        var held = await _context.HeldSales.FindAsync(id);
        if (held is null) return;
        _context.HeldSales.Remove(held);
        await _context.SaveChangesAsync();
    }
}
