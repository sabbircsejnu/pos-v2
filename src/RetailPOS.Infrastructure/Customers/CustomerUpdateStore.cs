using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Sales;

namespace RetailPOS.Infrastructure.Customers;

public sealed class CustomerUpdateStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : ICustomerUpdateStore
{
    public async Task<CustomerUpdateSnapshot?> GetForUpdateAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var customer = await dbContext.Customers
            .Include(entity => entity.Sales)
            .FirstOrDefaultAsync(entity => entity.Id == id && entity.BusinessId == businessId, cancellationToken);

        return customer is null
            ? null
            : new CustomerUpdateSnapshot(ToReadModel(customer), customer.IsSystem);
    }

    public async Task<CustomerReadModel?> FindByNormalizedPhoneAsync(
        string normalizedPhone,
        long excludingCustomerId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return await dbContext.Customers
            .AsNoTracking()
            .Where(customer =>
                customer.BusinessId == businessId &&
                customer.Id != excludingCustomerId &&
                customer.PhoneNormalized == normalizedPhone)
            .Select(CustomerReadStore.ToReadModel())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> EmailExistsAsync(
        string email,
        long excludingCustomerId,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var normalizedEmail = email.ToLower();
        return dbContext.Customers.AnyAsync(
            customer =>
                customer.BusinessId == businessId &&
                customer.Id != excludingCustomerId &&
                customer.Email != null &&
                customer.Email.ToLower() == normalizedEmail,
            cancellationToken);
    }

    public async Task<CustomerReadModel> UpdateAsync(
        CustomerUpdateValues values,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var customer = dbContext.Customers.Local
            .FirstOrDefault(entity => entity.Id == values.Id && entity.BusinessId == businessId)
            ?? await dbContext.Customers
                .Include(entity => entity.Sales)
                .FirstAsync(entity => entity.Id == values.Id && entity.BusinessId == businessId, cancellationToken);

        customer.Name = values.Name;
        customer.Phone = values.Phone;
        customer.PhoneNormalized = values.PhoneNormalized;
        customer.Email = values.Email;

        dbContext.Customers.Update(customer);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            values.PhoneNormalized is not null && PostgresViolations.IsUniqueViolation(exception, "phone_normalized"))
        {
            var existing = await FindByNormalizedPhoneAsync(values.PhoneNormalized, values.Id, cancellationToken);
            if (existing is not null)
                throw new CustomerPhoneConflictException(existing);
            throw;
        }

        return ToReadModel(customer);
    }

    private static CustomerReadModel ToReadModel(Customer customer) =>
        new(
            customer.Id,
            customer.Name,
            customer.Phone,
            customer.Email,
            customer.LoyaltyPoints,
            customer.CreatedAt,
            customer.Sales.Count);
}
