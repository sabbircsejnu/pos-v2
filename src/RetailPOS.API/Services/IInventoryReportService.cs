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
}
