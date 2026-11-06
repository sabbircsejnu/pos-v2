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

    /// <summary>
    /// POS Checkout picker: active customers of the caller's Business matched by name or phone only (never email),
    /// ordered by name and bounded by <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<CustomerReadModel>> QuickSearchByNameOrPhoneAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<CustomerReadModel?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken);
}
