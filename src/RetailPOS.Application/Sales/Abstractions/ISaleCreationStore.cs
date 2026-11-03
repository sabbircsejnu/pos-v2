using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Sales.Abstractions;

/// <summary>An already-committed sale found for an idempotency key, with the fingerprint of the request that created it.</summary>
public sealed record SaleIdempotencyMatch(SaleReadModel Sale, string? RequestFingerprint);

public sealed record ExchangeOriginalSale(Sale Sale, long? BusinessId);

public interface ISaleCreationStore
{
    Task<ISaleCreationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<SaleIdempotencyMatch?> FindByIdempotencyKeyAsync(long outletId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>True when the exception is the unique-index violation raised by a concurrent sale with the same outlet + idempotency key.</summary>
    bool IsIdempotencyKeyViolation(Exception exception);

    Task<Customer?> FindCustomerAsync(long customerId, long businessId, CancellationToken cancellationToken);
    Task<Customer?> FindWalkInCustomerAsync(long businessId, CancellationToken cancellationToken);
    Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken);
    Task<RetailPOS.Core.Entities.Inventory?> FindOutletInventoryAsync(
        long variantId,
        long outletId,
        CancellationToken cancellationToken);
    Task<long?> ResolveActiveTerminalIdAsync(long outletId, long? requestedTerminalId, CancellationToken cancellationToken);

    /// <summary>Returns the variant IDs that cannot be sold: unknown, or belonging to a product that is not active.</summary>
    Task<IReadOnlyList<long>> FindUnsellableVariantIdsAsync(IReadOnlyCollection<long> variantIds, long businessId, CancellationToken cancellationToken);

    Task<decimal> ResolveHistoricalUnitCostSnapshotAsync(long variantId, CancellationToken cancellationToken);
    Task<Sale?> FindSaleForUpdateAsync(long saleId, CancellationToken cancellationToken);

    /// <summary>Loads the original sale of an exchange for update (row-locked on PostgreSQL) together with the business that owns its outlet.</summary>
    Task<ExchangeOriginalSale?> FindExchangeOriginalSaleForUpdateAsync(long saleId, CancellationToken cancellationToken);

    Task<long?> GetOutletBusinessIdAsync(long outletId, CancellationToken cancellationToken);

    /// <summary>True when the user is active, belongs to the Business and is assigned to the outlet (primary or active assignment).</summary>
    Task<bool> IsEligibleSalespersonAsync(long userId, long businessId, long outletId, CancellationToken cancellationToken);
    Task<Customer?> FindCustomerForUpdateAsync(long customerId, CancellationToken cancellationToken);
    Task AddSaleAsync(Sale sale, CancellationToken cancellationToken);
    Task<SaleReadModel?> FindCreatedSaleAsync(long saleId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task SaveFinalChangesAsync(CancellationToken cancellationToken);
}

public interface ISaleCreationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

public sealed class SaleInventoryConcurrencyException(Exception innerException)
    : Exception("Sale inventory was modified concurrently.", innerException);
