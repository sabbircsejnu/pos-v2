namespace RetailPOS.Application.Pos.Abstractions;

/// <summary>
/// Versions the POS price and lookup caches per Business (and optionally per Outlet). The token is part of every cache
/// key, so bumping it orphans all earlier entries at once without scanning or deleting them.
/// </summary>
public interface IPosPricingCacheVersion
{
    /// <summary>
    /// The token to embed in cache keys, or <c>null</c> when the cache backend cannot be reached; callers must then bypass
    /// the cache and read the database.
    /// </summary>
    Task<string?> GetTokenAsync(long businessId, long? outletId, CancellationToken cancellationToken);

    /// <summary>
    /// Orphans cached prices and lookups of the Business, or only of one Outlet when <paramref name="outletId"/> is supplied.
    /// Best effort: a backend failure is logged and never fails the business operation that triggered it.
    /// </summary>
    Task InvalidateAsync(long businessId, long? outletId, CancellationToken cancellationToken);
}
