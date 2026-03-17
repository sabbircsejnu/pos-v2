using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

public class SupplierRepository : ISupplierRepository
{
    private readonly RetailPOSDbContext _context;

    public SupplierRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Supplier?> GetByIdAsync(long id)
    {
        return await _context.Suppliers
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<IEnumerable<Supplier>> GetAllAsync()
    {
        return await _context.Suppliers
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<(IEnumerable<Supplier> Suppliers, int TotalCount)> SearchAsync(
        string? searchQuery,
        decimal? minCreditLimit,
        decimal? maxCreditLimit,
        int pageNumber,
        int pageSize,
        string sortBy,
        string sortOrder)
    {
        var query = _context.Suppliers.AsQueryable();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var search = searchQuery.ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(search) ||
                (s.Contact != null && s.Contact.ToLower().Contains(search)) ||
                (s.Address != null && s.Address.ToLower().Contains(search)));
        }

        // Apply credit limit filters
        if (minCreditLimit.HasValue)
        {
            query = query.Where(s => s.CreditLimit >= minCreditLimit.Value);
        }

        if (maxCreditLimit.HasValue)
        {
            query = query.Where(s => s.CreditLimit <= maxCreditLimit.Value);
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(s => s.Name)
                : query.OrderBy(s => s.Name),
            "creditlimit" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(s => s.CreditLimit)
                : query.OrderBy(s => s.CreditLimit),
            "createdat" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(s => s.CreatedAt)
                : query.OrderBy(s => s.CreatedAt),
            _ => query.OrderBy(s => s.Name)
        };

        // Apply pagination
        var suppliers = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (suppliers, totalCount);
    }

    public async Task<Supplier> CreateAsync(Supplier supplier)
    {
        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();
        return supplier;
    }

    public async Task<Supplier> UpdateAsync(Supplier supplier)
    {
        supplier.UpdatedAt = DateTime.UtcNow;
        _context.Suppliers.Update(supplier);
        await _context.SaveChangesAsync();
        return supplier;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var supplier = await GetByIdAsync(id);
        if (supplier == null) return false;

        _context.Suppliers.Remove(supplier);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> NameExistsAsync(string name, long? excludeId = null)
    {
        return await _context.Suppliers
            .Where(s => s.Name.ToLower() == name.ToLower() && (excludeId == null || s.Id != excludeId))
            .AnyAsync();
    }

    public async Task<int> GetTotalPurchaseOrdersAsync(long supplierId)
    {
        return await _context.PurchaseOrders
            .Where(po => po.SupplierId == supplierId)
            .CountAsync();
    }

    public async Task<int> GetTotalBillsAsync(long supplierId)
    {
        return await _context.Bills
            .Where(b => b.SupplierId == supplierId)
            .CountAsync();
    }

    public async Task<decimal> GetTotalPurchaseAmountAsync(long supplierId)
    {
        return await _context.PurchaseOrders
            .Where(po => po.SupplierId == supplierId)
            .SumAsync(po => po.TotalAmount);
    }

    public async Task<decimal> GetOutstandingBalanceAsync(long supplierId)
    {
        return await _context.Bills
            .Where(b => b.SupplierId == supplierId && b.Status != "paid")
            .SumAsync(b => b.AmountDue);
    }
}
