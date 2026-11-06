using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Customers;

/// <summary>Reads customers of the caller's Business only. Another Business's customer is indistinguishable from a missing one.</summary>
public sealed class CustomerReadStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : ICustomerReadStore
{
    public async Task<(IReadOnlyList<CustomerReadModel> Customers, int TotalCount)> SearchAsync(
        string? query,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var customers = Scoped().Where(customer => customer.IsActive);

        if (!string.IsNullOrWhiteSpace(query))
            customers = ApplyTextFilter(customers, query);

        var totalCount = await customers.CountAsync(cancellationToken);
        var page = await customers
            .OrderByDescending(customer => customer.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ToReadModel())
            .ToListAsync(cancellationToken);

        return (page, totalCount);
    }

    public async Task<IReadOnlyList<CustomerReadModel>> QuickSearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken) =>
        await ApplyTextFilter(Scoped().Where(customer => customer.IsActive), query)
            .OrderBy(customer => customer.Name)
            .Take(limit)
            .Select(ToReadModel())
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerReadModel>> QuickSearchByNameOrPhoneAsync(
        string query,
        int limit,
        CancellationToken cancellationToken) =>
        await ApplyTextFilter(Scoped().Where(customer => customer.IsActive), query, matchEmail: false)
            .OrderBy(customer => customer.Name)
            .Take(limit)
            .Select(ToReadModel())
            .ToListAsync(cancellationToken);

    public async Task<CustomerReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken) =>
        await Scoped()
            .Where(customer => customer.Id == id)
            .Select(ToReadModel())
            .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<Customer> Scoped()
    {
        var businessId = businessScope.RequireBusinessId();
        return dbContext.Customers.AsNoTracking().Where(customer => customer.BusinessId == businessId);
    }

    private static IQueryable<Customer> ApplyTextFilter(IQueryable<Customer> customers, string query, bool matchEmail = true)
    {
        var lower = query.ToLower();
        var digits = CustomerPhoneNormalizer.DigitsOnly(query);
        return customers.Where(customer =>
            (customer.Name != null && customer.Name.ToLower().Contains(lower)) ||
            (customer.Phone != null && customer.Phone.Contains(lower)) ||
            (digits.Length >= 3 && customer.PhoneNormalized != null && customer.PhoneNormalized.Contains(digits)) ||
            (matchEmail && customer.Email != null && customer.Email.ToLower().Contains(lower)));
    }

    internal static System.Linq.Expressions.Expression<Func<Customer, CustomerReadModel>> ToReadModel() =>
        customer => new CustomerReadModel(
            customer.Id,
            customer.Name,
            customer.Phone,
            customer.Email,
            customer.LoyaltyPoints,
            customer.CreatedAt,
            customer.Sales.Count);
}
