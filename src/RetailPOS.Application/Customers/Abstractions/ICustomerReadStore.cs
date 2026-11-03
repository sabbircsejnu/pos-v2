namespace RetailPOS.Application.Customers.Abstractions;

public interface ICustomerReadStore
{
    Task<(IReadOnlyList<CustomerReadModel> Customers, int TotalCount)> SearchAsync(
        string? query,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerReadModel>> QuickSearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<CustomerReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken);
}
