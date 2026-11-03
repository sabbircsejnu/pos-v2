using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Customers;
using RetailPOS.Application.Customers.Abstractions;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Customers;

public sealed class CustomerDeleteStore(RetailPOSDbContext dbContext, IBusinessScope businessScope)
    : ICustomerDeleteStore
{
    public async Task<CustomerDeleteSnapshot?> GetForDeleteAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var customer = await dbContext.Customers
            .Include(entity => entity.Sales)
            .FirstOrDefaultAsync(entity => entity.Id == id && entity.BusinessId == businessId, cancellationToken);

        return customer is null ? null : new CustomerDeleteSnapshot(customer.IsSystem);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var customer = dbContext.Customers.Local
            .FirstOrDefault(entity => entity.Id == id && entity.BusinessId == businessId)
            ?? await dbContext.Customers.FirstOrDefaultAsync(
                entity => entity.Id == id && entity.BusinessId == businessId,
                cancellationToken);
        if (customer is null)
            return false;

        dbContext.Customers.Remove(customer);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
