using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Sales;

namespace RetailPOS.Infrastructure.Customers;

public sealed class CustomerCreateStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : ICustomerCreateStore
{
    public async Task<CustomerReadModel?> FindByNormalizedPhoneAsync(
        string normalizedPhone,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        return await dbContext.Customers
            .AsNoTracking()
            .Where(customer => customer.BusinessId == businessId && customer.PhoneNormalized == normalizedPhone)
            .Select(CustomerReadStore.ToReadModel())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var normalizedEmail = email.ToLower();
        return dbContext.Customers.AnyAsync(
            customer => customer.BusinessId == businessId &&
                        customer.Email != null && customer.Email.ToLower() == normalizedEmail,
            cancellationToken);
    }

    public async Task<CustomerReadModel> CreateAsync(
        CustomerCreateValues values,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var customer = new Customer
        {
            BusinessId = businessId,
            Name = values.Name,
            CustomerCode = null,
            IsSystem = false,
            IsActive = true,
            Phone = values.Phone,
            PhoneNormalized = values.PhoneNormalized,
            Email = values.Email,
            LoyaltyPoints = 0,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Customers.Add(customer);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            values.PhoneNormalized is not null && PostgresViolations.IsUniqueViolation(exception, "phone_normalized"))
        {
            // Two concurrent Quick Adds of the same number: report the winner instead of a database error.
            dbContext.Entry(customer).State = EntityState.Detached;
            var existing = await FindByNormalizedPhoneAsync(values.PhoneNormalized, cancellationToken);
            if (existing is not null)
                throw new CustomerPhoneConflictException(existing);
            throw;
        }

        return new CustomerReadModel(
            customer.Id,
            customer.Name,
            customer.Phone,
            customer.Email,
            customer.LoyaltyPoints,
            customer.CreatedAt,
            0);
    }
}
