using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Customers;

public sealed class CustomerLoyaltyStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : ICustomerLoyaltyStore
{
    public async Task<CustomerLoyaltySnapshot?> GetForLoyaltyUpdateAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var customer = await dbContext.Customers
            .Include(entity => entity.Sales)
            .FirstOrDefaultAsync(entity => entity.Id == id && entity.BusinessId == businessId, cancellationToken);

        return customer is null
            ? null
            : new CustomerLoyaltySnapshot(customer.Id, customer.LoyaltyPoints);
    }

    public async Task<CustomerReadModel> SetLoyaltyPointsAsync(
        long id,
        int loyaltyPoints,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var customer = dbContext.Customers.Local
            .FirstOrDefault(entity => entity.Id == id && entity.BusinessId == businessId)
            ?? await dbContext.Customers
                .Include(entity => entity.Sales)
                .FirstAsync(entity => entity.Id == id && entity.BusinessId == businessId, cancellationToken);

        customer.LoyaltyPoints = loyaltyPoints;
        dbContext.Customers.Update(customer);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CustomerReadModel(
            customer.Id,
            customer.Name,
            customer.Phone,
            customer.Email,
            customer.LoyaltyPoints,
            customer.CreatedAt,
            customer.Sales.Count);
    }
}
