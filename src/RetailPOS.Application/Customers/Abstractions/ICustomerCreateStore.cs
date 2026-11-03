using RetailPOS.Application.Customers;

namespace RetailPOS.Application.Customers.Abstractions;

public interface ICustomerCreateStore
{
    /// <summary>The customer of the caller's Business that already owns this normalized phone, if any.</summary>
    Task<CustomerReadModel?> FindByNormalizedPhoneAsync(string normalizedPhone, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the customer in the caller's Business. A concurrent insert of the same normalized phone surfaces as
    /// <see cref="CustomerPhoneConflictException"/>.
    /// </summary>
    Task<CustomerReadModel> CreateAsync(
        CustomerCreateValues values,
        CancellationToken cancellationToken);
}
