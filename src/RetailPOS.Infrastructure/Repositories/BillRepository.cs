using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Bill data access operations
/// </summary>
public class BillRepository : IBillRepository
{
    private readonly RetailPOSDbContext _context;

    public BillRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Bill?> GetByIdAsync(long id)
    {
        return await _context.Bills
            .Include(b => b.Supplier)
            .Include(b => b.PurchaseOrder)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<(IEnumerable<Bill>, int)> SearchAsync(string? status, long? supplierId, int pageNumber, int pageSize)
    {
        var query = _context.Bills
            .Include(b => b.Supplier)
            .Include(b => b.PurchaseOrder)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(b => b.Status == status.ToLower());

        if (supplierId.HasValue)
            query = query.Where(b => b.SupplierId == supplierId.Value);

        var totalCount = await query.CountAsync();

        var bills = await query
            .OrderByDescending(b => b.DueDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (bills, totalCount);
    }

    public async Task<Bill> CreateAsync(Bill bill)
    {
        _context.Bills.Add(bill);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(bill.Id))!;
    }

    public async Task<Bill> UpdateAsync(Bill bill)
    {
        _context.Bills.Update(bill);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(bill.Id))!;
    }

    public async Task<IEnumerable<Bill>> GetOverdueAsync()
    {
        var now = DateTime.UtcNow;
        return await _context.Bills
            .Include(b => b.Supplier)
            .Where(b => b.DueDate < now && b.Status != "paid")
            .OrderBy(b => b.DueDate)
            .ToListAsync();
    }

    public async Task<(decimal totalUnpaid, decimal totalOverdue, int unpaidCount, int overdueCount)> GetSummaryAsync()
    {
        var now = DateTime.UtcNow;

        var unpaidBills = await _context.Bills
            .Where(b => b.Status != "paid")
            .ToListAsync();

        var totalUnpaid = unpaidBills.Sum(b => b.AmountDue);
        var unpaidCount = unpaidBills.Count;

        var overdueBills = unpaidBills.Where(b => b.DueDate < now).ToList();
        var totalOverdue = overdueBills.Sum(b => b.AmountDue);
        var overdueCount = overdueBills.Count;

        return (totalUnpaid, totalOverdue, unpaidCount, overdueCount);
    }
}
