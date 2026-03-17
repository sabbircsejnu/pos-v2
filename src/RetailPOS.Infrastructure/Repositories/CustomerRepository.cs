using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Customer data access operations
/// </summary>
public class CustomerRepository : ICustomerRepository
{
    private readonly RetailPOSDbContext _context;

    public CustomerRepository(RetailPOSDbContext context)
    {
        _context = context;
    }

    public async Task<Customer?> GetByIdAsync(long id)
    {
        return await _context.Customers
            .Include(c => c.Sales)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<(IEnumerable<Customer>, int)> SearchAsync(string? query, int pageNumber, int pageSize)
    {
        var q = _context.Customers
            .Include(c => c.Sales)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var lower = query.ToLower();
            q = q.Where(c =>
                (c.Name != null && c.Name.ToLower().Contains(lower)) ||
                (c.Phone != null && c.Phone.Contains(lower)) ||
                (c.Email != null && c.Email.ToLower().Contains(lower)));
        }

        var totalCount = await q.CountAsync();

        var customers = await q
            .OrderByDescending(c => c.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (customers, totalCount);
    }

    public async Task<IEnumerable<Customer>> QuickSearchAsync(string query, int limit = 10)
    {
        var lower = query.ToLower();
        return await _context.Customers
            .Where(c =>
                (c.Name != null && c.Name.ToLower().Contains(lower)) ||
                (c.Phone != null && c.Phone.Contains(lower)) ||
                (c.Email != null && c.Email.ToLower().Contains(lower)))
            .OrderBy(c => c.Name)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<Customer?> GetByPhoneAsync(string phone)
    {
        return await _context.Customers
            .FirstOrDefaultAsync(c => c.Phone == phone);
    }

    public async Task<Customer?> GetByEmailAsync(string email)
    {
        return await _context.Customers
            .FirstOrDefaultAsync(c => c.Email != null && c.Email.ToLower() == email.ToLower());
    }

    public async Task<Customer> CreateAsync(Customer customer)
    {
        customer.CreatedAt = DateTime.UtcNow;
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(customer.Id))!;
    }

    public async Task<Customer> UpdateAsync(Customer customer)
    {
        _context.Customers.Update(customer);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(customer.Id))!;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return false;

        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync();
        return true;
    }
}
