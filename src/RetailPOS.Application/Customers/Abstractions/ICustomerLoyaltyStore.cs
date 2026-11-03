using RetailPOS.Application.Customers;

namespace RetailPOS.Application.Customers.Abstractions;

public interface ICustomerLoyaltyStore
{
    Task<CustomerLoyaltySnapshot?> GetForLoyaltyUpdateAsync(
        long id,
        CancellationToken cancellationToken);

    Task<CustomerReadModel> SetLoyaltyPointsAsync(
        long id,
        int loyaltyPoints,
        CancellationToken cancellationToken);
}
