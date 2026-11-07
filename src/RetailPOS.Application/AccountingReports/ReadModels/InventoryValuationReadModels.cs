namespace RetailPOS.Application.AccountingReports.ReadModels;

public sealed record InventoryValuationFilter(
    DateTime? DateTo,
    long? OutletId,
    long? WarehouseId,
    long? LocationId,
    string? LocationType,
    long? ProductId,
    long? VariantId);

public sealed record InventoryValuationReadModel(
    IReadOnlyList<InventoryValuationRowReadModel> Rows,
    IReadOnlyList<UnvaluedStockMovementReadModel> UnvaluedMovements,
    decimal TotalQuantity,
    decimal TotalInventoryValue,
    decimal GeneralLedgerBalance,
    decimal ReconciliationDifference,
    long InventoryAccountId,
    DateTime AsOfDate);

public sealed record InventoryValuationRowReadModel(
    long VariantId,
    long ProductId,
    string ProductName,
    string? ProductCode,
    string Sku,
    long LocationId,
    string LocationType,
    decimal Quantity,
    decimal UnitCost,
    decimal InventoryValue);

public sealed record UnvaluedStockMovementReadModel(
    long MovementId,
    long VariantId,
    long LocationId,
    string LocationType,
    DateTime BusinessDate,
    string ReferenceType,
    long ReferenceId,
    string? Remarks);
