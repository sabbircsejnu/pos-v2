// =====================================================================
// NEW — Stock Ledger repository interface
// =====================================================================
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IStockLedgerRepository
{
    /// <summary>Returns a single ledger entry by its primary key.</summary>
    Task<StockLedger?> GetByIdAsync(long id);

    /// <summary>
    /// Returns the full movement history for a specific variant at a specific location,
    /// ordered chronologically (oldest first).
    /// </summary>
    Task<List<StockLedger>> GetByVariantAndLocationAsync(long variantId, long locationId, string locationType);

    /// <summary>
    /// Returns all ledger rows that belong to one source document
    /// (e.g. all rows for GRN #42, or all rows for Sale #7).
    /// </summary>
    Task<List<StockLedger>> GetByReferenceAsync(string referenceType, long referenceId);

    /// <summary>
    /// Paginated search with optional filters for variant, location, transaction type,
    /// reference document and date range.
    /// </summary>
    Task<(List<StockLedger> Entries, int TotalCount)> SearchAsync(
        long? variantId,
        long? locationId,
        string? locationType,
        string? transactionType,
        string? referenceType,
        long? referenceId,
        DateTime? startDate,
        DateTime? endDate,
        int pageNumber,
        int pageSize);
}
