namespace RetailPOS.Application.Inventory.StockLedger;

public sealed record StockLedgerLocationScope(long Id, string Type);

public sealed record StockLedgerReadScope(
    long BusinessId,
    IReadOnlyList<StockLedgerLocationScope> Locations);

public sealed record StockLedgerReadModel(
    long Id,
    long VariantId,
    string VariantSku,
    string ProductName,
    long LocationId,
    string LocationType,
    string LocationName,
    string TransactionType,
    int QtyIn,
    int QtyOut,
    int BalanceAfter,
    decimal UnitCost,
    decimal MovementValue,
    bool IsCostValuationReliable,
    DateTime BusinessDate,
    long MovementSequence,
    long? ReversalOfMovementId,
    string ReferenceType,
    long ReferenceId,
    string? Remarks,
    long? CreatedBy,
    string? CreatedByName,
    DateTime CreatedAt);

public sealed record StockLedgerSearchFilter(
    long? VariantId,
    long? LocationId,
    string? LocationType,
    string? TransactionType,
    string? ReferenceType,
    long? ReferenceId,
    DateTime? StartDate,
    DateTime? EndDate,
    int PageNumber,
    int PageSize);

public sealed record StockLedgerSearchReadModel(
    IReadOnlyList<StockLedgerReadModel> Entries,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);
