namespace RetailPOS.Application.Grns.Abstractions;

/// <summary>
/// The Business and locations whose GRNs the current user may read. Owners and Super Admins hold
/// <see cref="HasAllLocations"/>; everyone else is limited to their Step 5 assignments.
/// </summary>
public sealed record GrnReadScope(
    long BusinessId,
    bool HasAllLocations,
    IReadOnlyList<long> OutletIds,
    IReadOnlyList<long> WarehouseIds)
{
    public bool AllowsOutlet(long outletId) => OutletIds.Contains(outletId);

    public bool AllowsWarehouse(long warehouseId) => WarehouseIds.Contains(warehouseId);
}

public interface IGrnReadScopeProvider
{
    Task<GrnReadScope> GetAsync(CancellationToken cancellationToken);
}

/// <summary>A half-open UTC range: from is inclusive, to is exclusive. Either bound may be open.</summary>
public sealed record GrnReceivedWindow(DateTime? FromUtc, DateTime? ToExclusiveUtc);

public sealed record GrnSearchCriteria(
    long? PurchaseOrderId,
    string? Status,
    DateTime? StartDate,
    DateTime? EndDate,
    string? LocationType,
    long? LocationId,
    GrnReceivedWindow? ReceivedDate,
    int PageNumber,
    int PageSize);
