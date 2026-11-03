namespace RetailPOS.Application.Customers.Abstractions;

public interface ICustomerDeleteStore
{
    Task<CustomerDeleteSnapshot?> GetForDeleteAsync(
        long id,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        long id,
        CancellationToken cancellationToken);
}
