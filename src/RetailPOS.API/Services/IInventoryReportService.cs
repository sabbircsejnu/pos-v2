using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

/// <summary>
/// Interface for inventory reporting queries
/// </summary>
public interface IInventoryReportService
{
    /// <summary>Returns stock levels, optionally filtered by location or low-stock status</summary>
    Task<List<StockLevelDto>> GetStockLevelsAsync(long? locationId, string? locationType, bool lowStockOnly);

    /// <summary>Returns overall inventory valuation with category breakdown</summary>
    Task<InventoryValuationDto> GetInventoryValuationAsync();

    /// <summary>Returns items with no sales activity within the specified number of days</summary>
    Task<List<SlowMovingItemDto>> GetSlowMovingItemsAsync(int days = 90);

    /// <summary>
    /// Full stock movement history for a product (all variants) or a single variant
    /// within a date window, including opening/closing balance summary.
    /// </summary>
    Task<StockTransactionReportDto> GetStockTransactionReportAsync(
        long productId,
        long? variantId,
        long? locationId,
        string? locationType,
        DateTime? dateFrom,
        DateTime? dateTo);

    /// <summary>
    /// Current stock report: paginated rows + full-dataset summary.
    /// Summary totals are always computed from the complete filtered set,
    /// independent of page/pageSize.
    /// </summary>
    Task<CurrentStockReportDto> GetCurrentStockReportAsync(CurrentStockFilterDto filter);

    /// <summary>
    /// Returns ALL rows for the current stock report (used for export).
    /// No pagination applied — filter still applies in full.
    /// </summary>
    Task<List<CurrentStockRowDto>> GetCurrentStockExportAsync(CurrentStockFilterDto filter);

    /// <summary>
    /// Product Ledger Report: complete stock movement history for a product,
    /// paginated. Summary totals (opening, stock in/out, closing, value) are
    /// always computed from the complete filtered dataset.
    /// </summary>
    Task<ProductLedgerReportDto> GetProductLedgerReportAsync(ProductLedgerFilterDto filter);

    /// <summary>
    /// Returns ALL ledger rows for export (no pagination).
    /// </summary>
    Task<List<ProductLedgerRowDto>> GetProductLedgerExportAsync(ProductLedgerFilterDto filter);

    /// <summary>
    /// Stock Movement Report: aggregated opening/in/out/closing per variant+location
    /// for a date period. Summary totals are always computed from the complete filtered set.
    /// </summary>
    Task<StockMovementReportDto> GetStockMovementReportAsync(StockMovementFilterDto filter);

    /// <summary>Returns ALL movement rows for export (no pagination).</summary>
    Task<List<StockMovementRowDto>> GetStockMovementExportAsync(StockMovementFilterDto filter);

    /// <summary>
    /// Stock Valuation Report: inventory value per variant+location.
    /// Summary totals are always computed from the complete filtered set.
    /// </summary>
    Task<StockValuationReportDto> GetStockValuationReportAsync(StockValuationFilterDto filter);

    /// <summary>Returns ALL valuation rows for export (no pagination).</summary>
    Task<List<StockValuationRowDto>> GetStockValuationExportAsync(StockValuationFilterDto filter);

    /// <summary>
    /// Outlet Wise Stock Report: stock levels grouped by outlet, including per-outlet summary.
    /// Summary totals always reflect the full filtered dataset.
    /// </summary>
    Task<OutletWiseStockReportDto> GetOutletWiseStockReportAsync(OutletWiseStockFilterDto filter);

    /// <summary>Returns ALL outlet-wise rows for export (no pagination).</summary>
    Task<List<OutletWiseStockRowDto>> GetOutletWiseStockExportAsync(OutletWiseStockFilterDto filter);

    /// <summary>Low Stock Report: items where current stock &lt; reorder level.</summary>
    Task<LowStockReportDto> GetLowStockReportAsync(LowStockFilterDto filter);

    /// <summary>Returns ALL low-stock rows for export (no pagination).</summary>
    Task<List<LowStockRowDto>> GetLowStockExportAsync(LowStockFilterDto filter);

    /// <summary>Out Of Stock Report: items where current stock = 0.</summary>
    Task<OutOfStockReportDto> GetOutOfStockReportAsync(OutOfStockFilterDto filter);

    /// <summary>Returns ALL out-of-stock rows for export (no pagination).</summary>
    Task<List<OutOfStockRowDto>> GetOutOfStockExportAsync(OutOfStockFilterDto filter);

    /// <summary>Negative Stock Report: items where current stock &lt; 0.</summary>
    Task<NegativeStockReportDto> GetNegativeStockReportAsync(NegativeStockFilterDto filter);

    /// <summary>Returns ALL negative-stock rows for export (no pagination).</summary>
    Task<List<NegativeStockRowDto>> GetNegativeStockExportAsync(NegativeStockFilterDto filter);

    /// <summary>Stock Adjustment Report: all manual adjustment records in a date range.</summary>
    Task<StockAdjustmentReportDto> GetStockAdjustmentReportAsync(StockAdjustmentFilterDto filter);

    /// <summary>Returns ALL adjustment rows for export (no pagination).</summary>
    Task<List<StockAdjustmentRowDto>> GetStockAdjustmentExportAsync(StockAdjustmentFilterDto filter);

    /// <summary>Stock Transfer Report: all inter-location transfer line items in a date range.</summary>
    Task<StockTransferReportDto> GetStockTransferReportAsync(StockTransferFilterDto filter);

    /// <summary>Returns ALL transfer rows for export (no pagination).</summary>
    Task<List<StockTransferRowDto>> GetStockTransferReportExportAsync(StockTransferFilterDto filter);
}
