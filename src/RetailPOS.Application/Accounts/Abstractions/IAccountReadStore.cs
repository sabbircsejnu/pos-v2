using RetailPOS.Application.Accounts;

namespace RetailPOS.Application.Accounts.Abstractions;

public interface IAccountReadStore
{
    Task<IReadOnlyList<AccountReadModel>> GetAllAsync(
        long? businessId,
        string? type,
        CancellationToken cancellationToken);

    Task<AccountReadModel?> GetByIdAsync(
        long? businessId,
        long accountId,
        CancellationToken cancellationToken);
}

public interface IAccountReadScope
{
    long? BusinessId { get; }
}
