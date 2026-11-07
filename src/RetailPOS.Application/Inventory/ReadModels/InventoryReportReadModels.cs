namespace RetailPOS.Application.Inventory.ReadModels;

public sealed record InventoryReportReadScope(
    long? BusinessId,
    IReadOnlyList<long> OutletIds,
    IReadOnlyList<long> WarehouseIds);

public sealed record StockLevelReadModel(
    long VariantId,
    string ProductName,
    string Sku,
    string CategoryName,
    long LocationId,
    string LocationType,
    string LocationName,
    decimal Quantity,
    int ReorderLevel,
    bool IsLowStock,
    decimal EstimatedValue);

public sealed record InventoryReportCategoryValuationReadModel(string CategoryName, decimal Value, decimal ItemCount);

public sealed record InventoryReportValuationReadModel(
    decimal TotalValue,
    decimal TotalItems,
    IReadOnlyList<InventoryReportCategoryValuationReadModel> ByCategory);

public sealed record SlowMovingItemReadModel(
    long VariantId,
    string ProductName,
    string Sku,
    decimal CurrentStock,
    int DaysSinceLastSale,
    decimal EstimatedValue);

public sealed record CurrentStockReportFilter(
    long? OutletId,
    long? WarehouseId,
    long? CategoryId,
    long? ProductId,
    string? StockStatus,
    string? Search,
    string? SortBy,
    string? SortDir,
    int Page,
    int PageSize);

public sealed record CurrentStockRowReadModel(
    long VariantId,
    string ProductCode,
    string? Barcode,
    string ProductName,
    string Category,
    long LocationId,
    string LocationType,
    string LocationName,
    decimal AvailableQuantity,
    decimal ReservedQuantity,
    int ReorderLevel,
    decimal UnitCost,
    decimal StockValue,
    DateTime? LastPurchaseDate,
    DateTime? LastSaleDate,
    string StockStatus);

public sealed record CurrentStockSummaryReadModel(
    int TotalProducts,
    decimal TotalQuantity,
    decimal TotalStockValue);

public sealed record CurrentStockReportReadModel(
    IReadOnlyList<CurrentStockRowReadModel> Items,
    int TotalCount,
    CurrentStockSummaryReadModel Summary);

public sealed record ProductLedgerReportFilter(
    long ProductId,
    long? VariantId,
    long? OutletId,
    long? WarehouseId,
    DateTime? DateFrom,
    DateTime? DateTo,
    string? TransactionType,
    int Page,
    int PageSize);

public sealed record ProductLedgerRowReadModel(
    long LedgerId,
    DateTime TransactionDate,
    string TransactionType,
    string TransactionTypeLabel,
    string ReferenceNumber,
    long VariantId,
    string ProductName,
    string Sku,
    long LocationId,
    string LocationName,
    decimal OpeningQuantity,
    decimal StockIn,
    decimal StockOut,
    decimal ClosingQuantity,
    decimal UnitCost,
    decimal TransactionValue,
    string? PerformedBy,
    string? Remarks);

public sealed record ProductLedgerSummaryReadModel(
    decimal OpeningStock,
    decimal TotalStockIn,
    decimal TotalStockOut,
    decimal ClosingStock,
    decimal TotalTransactionValue);

public sealed record ProductLedgerReportReadModel(
    long ProductId,
    string ProductName,
    string? ProductSku,
    long? VariantId,
    string? VariantName,
    IReadOnlyList<ProductLedgerRowReadModel> Items,
    int TotalCount,
    ProductLedgerSummaryReadModel Summary);

public sealed record StockTransactionReportFilter(
    long ProductId,
    long? VariantId,
    long? LocationId,
    string? LocationType,
    DateTime? DateFrom,
    DateTime? DateTo);

public sealed record StockTransactionReportRowReadModel(
    long Id,
    DateTime TransactionDate,
    long LocationId,
    string LocationType,
    string LocationName,
    string TransactionType,
    string ReferenceType,
    long ReferenceId,
    string ReferenceNumber,
    long VariantId,
    string? VariantCode,
    string? VariantAttributes,
    decimal QuantityIn,
    decimal QuantityOut,
    decimal RunningBalance,
    string? Remarks,
    string? CreatedByName);

public sealed record StockTransactionReportSummaryReadModel(
    decimal OpeningStock,
    decimal StockIn,
    decimal StockOut,
    decimal ClosingStock,
    decimal CurrentStock);

public sealed record StockTransactionReportReadModel(
    long ProductId,
    string ProductName,
    string ProductCode,
    string Level,
    long? VariantId,
    string? VariantCode,
    string? VariantName,
    string? VariantAttributes,
    string Sku,
    long? LocationId,
    string? LocationType,
    DateTime? DateFrom,
    DateTime? DateTo,
    StockTransactionReportSummaryReadModel Summary,
    IReadOnlyList<StockTransactionReportRowReadModel> Rows);

public sealed record StockMovementReportFilter(
    long? OutletId,
    long? WarehouseId,
    long? CategoryId,
    string? Search,
    DateTime? DateFrom,
    DateTime? DateTo,
    string SortBy,
    string SortDir,
    int Page,
    int PageSize);

public sealed record StockMovementRowReadModel(
    long VariantId,
    string ProductCode,
    string Sku,
    string ProductName,
    string VariantName,
    string? VariantAttributes,
    string CategoryName,
    long LocationId,
    string LocationType,
    string LocationName,
    decimal OpeningStock,
    decimal StockIn,
    decimal StockOut,
    decimal ClosingStock,
    decimal NetMovement);

public sealed record StockMovementSummaryReadModel(
    decimal TotalOpeningStock,
    decimal TotalStockIn,
    decimal TotalStockOut,
    decimal TotalClosingStock,
    decimal NetMovement);

public sealed record StockMovementReportReadModel(
    IReadOnlyList<StockMovementRowReadModel> Items,
    int TotalCount,
    StockMovementSummaryReadModel Summary);

public sealed record StockValuationReportFilter(
    long? OutletId,
    long? WarehouseId,
    long? CategoryId,
    string? Search,
    string SortBy,
    string SortDir,
    int Page,
    int PageSize);

public sealed record StockValuationRowReadModel(
    long VariantId,
    string ProductCode,
    string Barcode,
    string Sku,
    string ProductName,
    string CategoryName,
    long LocationId,
    string LocationType,
    string LocationName,
    decimal Quantity,
    decimal UnitCost,
    decimal InventoryValue,
    decimal PercentOfTotal);

public sealed record StockValuationCategoryReadModel(
    string CategoryName,
    decimal Quantity,
    decimal InventoryValue,
    decimal PercentOfTotal);

public sealed record StockValuationSummaryReadModel(
    int TotalProducts,
    decimal TotalQuantity,
    decimal TotalInventoryValue,
    IReadOnlyList<StockValuationCategoryReadModel> ByCategory);

public sealed record StockValuationReportReadModel(
    IReadOnlyList<StockValuationRowReadModel> Items,
    int TotalCount,
    StockValuationSummaryReadModel Summary);

public sealed record OutletWiseStockFilter(
    long? OutletId,
    long? CategoryId,
    string? Search,
    string SortBy,
    string SortDir,
    int Page,
    int PageSize);

public sealed record OutletWiseStockRowReadModel(
    long OutletId,
    string OutletName,
    long VariantId,
    string ProductCode,
    string Barcode,
    string Sku,
    string ProductName,
    string CategoryName,
    decimal Quantity,
    decimal UnitCost,
    decimal StockValue);

public sealed record OutletStockSummaryReadModel(
    long OutletId,
    string OutletName,
    int TotalSkus,
    decimal TotalQty,
    decimal StockValue);

public sealed record OutletWiseStockSummaryReadModel(
    int TotalOutlets,
    int TotalSkus,
    decimal TotalQuantity,
    decimal TotalStockValue,
    IReadOnlyList<OutletStockSummaryReadModel> ByOutlet);

public sealed record OutletWiseStockReportReadModel(
    IReadOnlyList<OutletWiseStockRowReadModel> Items,
    int TotalCount,
    OutletWiseStockSummaryReadModel Summary);

public sealed record LowStockReportFilter(
    long? OutletId,
    long? WarehouseId,
    long? CategoryId,
    string? Search,
    string SortBy,
    string SortDir,
    int Page,
    int PageSize);

public sealed record LowStockRowReadModel(
    long VariantId,
    string ProductCode,
    string Barcode,
    string Sku,
    string ProductName,
    string CategoryName,
    long LocationId,
    string LocationType,
    string LocationName,
    decimal CurrentStock,
    int ReorderLevel,
    decimal SuggestedOrderQty,
    string UrgencyLevel);

public sealed record LowStockSummaryReadModel(
    int TotalSkus,
    decimal TotalDeficitQty,
    int CriticalCount,
    int LowCount);

public sealed record LowStockReportReadModel(
    IReadOnlyList<LowStockRowReadModel> Items,
    int TotalCount,
    LowStockSummaryReadModel Summary);

public sealed record OutOfStockReportFilter(
    long? OutletId,
    long? WarehouseId,
    long? CategoryId,
    string? Search,
    string SortBy,
    string SortDir,
    int Page,
    int PageSize);

public sealed record OutOfStockRowReadModel(
    long VariantId,
    string ProductCode,
    string Barcode,
    string Sku,
    string ProductName,
    string CategoryName,
    long LocationId,
    string LocationType,
    string LocationName,
    int ReorderLevel,
    decimal UnitCost);

public sealed record OutOfStockSummaryReadModel(
    int TotalSkus,
    int TotalLocations,
    decimal EstimatedCostImpact);

public sealed record OutOfStockReportReadModel(
    IReadOnlyList<OutOfStockRowReadModel> Items,
    int TotalCount,
    OutOfStockSummaryReadModel Summary);

public sealed record NegativeStockReportFilter(
    long? OutletId,
    long? WarehouseId,
    long? CategoryId,
    string? Search,
    string SortBy,
    string SortDir,
    int Page,
    int PageSize);

public sealed record NegativeStockRowReadModel(
    long VariantId,
    string ProductCode,
    string Barcode,
    string Sku,
    string ProductName,
    string CategoryName,
    long LocationId,
    string LocationType,
    string LocationName,
    decimal CurrentStock,
    decimal UnitCost,
    decimal StockValue);

public sealed record NegativeStockSummaryReadModel(
    int TotalSkus,
    decimal TotalNegativeQty,
    decimal TotalNegativeValue);

public sealed record NegativeStockReportReadModel(
    IReadOnlyList<NegativeStockRowReadModel> Items,
    int TotalCount,
    NegativeStockSummaryReadModel Summary);

public sealed record StockAdjustmentReportFilter(
    long? OutletId,
    long? WarehouseId,
    long? CategoryId,
    string? Search,
    DateTime? DateFrom,
    DateTime? DateTo,
    string SortBy,
    string SortDir,
    int Page,
    int PageSize);

public sealed record StockAdjustmentRowReadModel(
    long Id,
    DateTime AdjustmentDate,
    string ProductCode,
    string Barcode,
    string Sku,
    string ProductName,
    string CategoryName,
    long LocationId,
    string LocationType,
    string LocationName,
    string AdjustmentType,
    decimal QuantityChange,
    string Reason,
    string AdjustedBy);

public sealed record StockAdjustmentSummaryReadModel(
    int TotalAdjustments,
    decimal TotalAdditions,
    decimal TotalReductions,
    decimal NetQuantityChange);

public sealed record StockAdjustmentReportReadModel(
    IReadOnlyList<StockAdjustmentRowReadModel> Items,
    int TotalCount,
    StockAdjustmentSummaryReadModel Summary);

public sealed record StockTransferReportFilter(
    long? FromOutletId,
    long? ToOutletId,
    long? CategoryId,
    string Status,
    string Search,
    DateTime? DateFrom,
    DateTime? DateTo,
    string SortBy,
    string SortDir,
    int Page,
    int PageSize);

public sealed record StockTransferReportRowReadModel(
    long TransferId,
    DateTime TransferDate,
    string FromLocationName,
    string FromLocationType,
    string ToLocationName,
    string ToLocationType,
    string Status,
    string ProductCode,
    string Barcode,
    string Sku,
    string ProductName,
    string CategoryName,
    decimal Quantity,
    decimal UnitCost,
    decimal TransferValue,
    string CreatedBy);

public sealed record StockTransferReportSummaryReadModel(
    int TotalTransfers,
    int TotalLines,
    decimal TotalQuantity,
    decimal TotalValue,
    int PendingCount,
    int CompletedCount);

public sealed record StockTransferReportReadModel(
    IReadOnlyList<StockTransferReportRowReadModel> Items,
    int TotalCount,
    StockTransferReportSummaryReadModel Summary);
