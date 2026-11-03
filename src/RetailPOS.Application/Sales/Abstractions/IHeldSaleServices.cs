using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Abstractions;

public interface IHeldSaleStore
{
    Task<long?> GetOutletIdAsync(long id, CancellationToken cancellationToken);
    Task<HeldSale?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<HeldSale>> GetByOutletAsync(long outletId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HeldSale>> GetByCashierAsync(
        long cashierId,
        IReadOnlyCollection<long> authorizedOutletIds,
        CancellationToken cancellationToken);
    Task<HeldSale> CreateAsync(HeldSale heldSale, CancellationToken cancellationToken);

    /// <summary>True when the customer belongs to the same Business as the outlet.</summary>
    Task<bool> CustomerBelongsToOutletBusinessAsync(long customerId, long outletId, CancellationToken cancellationToken);
    Task DeleteAsync(long id, CancellationToken cancellationToken);
}

public interface IHeldSaleOutletAuthorizer
{
    Task<long> AuthorizeReadAsync(long outletId, CancellationToken cancellationToken);
    Task<long> AuthorizeWriteAsync(long outletId, CancellationToken cancellationToken);
    Task<IReadOnlyList<long>> GetAuthorizedOutletIdsAsync(CancellationToken cancellationToken);
}
