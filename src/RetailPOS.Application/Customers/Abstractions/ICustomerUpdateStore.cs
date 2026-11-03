using RetailPOS.Application.Customers;

namespace RetailPOS.Application.Customers.Abstractions;

public interface ICustomerUpdateStore
{
    Task<CustomerUpdateSnapshot?> GetForUpdateAsync(
        long id,
        CancellationToken cancellationToken);

    Task<CustomerReadModel?> FindByNormalizedPhoneAsync(
        string normalizedPhone,
        long excludingCustomerId,
        CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(
        string email,
        long excludingCustomerId,
        CancellationToken cancellationToken);

    Task<CustomerReadModel> UpdateAsync(
        CustomerUpdateValues values,
        CancellationToken cancellationToken);
}
