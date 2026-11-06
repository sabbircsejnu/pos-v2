namespace RetailPOS.Application.Expenses.Abstractions;

/// <summary>
/// The locations whose expenses the current user may work with. Only a business-wide user can use a business-level
/// expense (no outlet and no warehouse).
/// </summary>
public sealed record ExpenseLocationScope(
    bool IsBusinessWide,
    IReadOnlyList<long> OutletIds,
    IReadOnlyList<long> WarehouseIds)
{
    public bool Allows(long? outletId, long? warehouseId)
    {
        if (outletId.HasValue)
            return OutletIds.Contains(outletId.Value);
        if (warehouseId.HasValue)
            return WarehouseIds.Contains(warehouseId.Value);
        return IsBusinessWide;
    }
}

public interface IExpenseLocationAuthorizer
{
    Task<ExpenseLocationScope> GetScopeAsync(CancellationToken cancellationToken);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> when the location is outside the user's scope.</summary>
    Task AuthorizeAsync(long? outletId, long? warehouseId, CancellationToken cancellationToken);
}

/// <summary>Issues the next expense reference; must run inside the transaction that persists the expense.</summary>
public interface IExpenseNumberAllocator
{
    Task<string> AllocateAsync(
        long businessId,
        long? outletId,
        long? warehouseId,
        long actorUserId,
        CancellationToken cancellationToken);
}
