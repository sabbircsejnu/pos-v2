namespace RetailPOS.Application.Sales.Abstractions;

/// <summary>
/// The Business and outlets whose sales the current user may read. <see cref="OutletIds"/> is never empty: a request
/// with no outlet means "all of my outlets", never "no restriction".
/// </summary>
public sealed record SaleReadScope(long BusinessId, IReadOnlyList<long> OutletIds);

public interface ISaleReadScopeProvider
{
    /// <summary>
    /// Resolves the scope from the authenticated user; a requested outlet can only narrow it.
    /// Throws <see cref="UnauthorizedAccessException"/> when the outlet is not allowed or the user has none.
    /// </summary>
    Task<SaleReadScope> GetAsync(long? requestedOutletId, CancellationToken cancellationToken);
}
