// =====================================================================
// NEW — Stock Ledger service interface
// =====================================================================
using RetailPOS.API.DTOs.StockLedger;

namespace RetailPOS.API.Services;

public interface IStockLedgerService
{
    // ── Query API ─────────────────────────────────────────────────────

    Task<StockLedgerDto?> GetByIdAsync(long id);

    /// <summary>Full movement history for a variant at a single location (oldest first).</summary>
    Task<List<StockLedgerDto>> GetByVariantAndLocationAsync(
        long variantId, long locationId, string locationType);

    /// <summary>All ledger rows produced by one source document.</summary>
    Task<List<StockLedgerDto>> GetByReferenceAsync(string referenceType, long referenceId);

    /// <summary>Paginated search with optional filters.</summary>
    Task<StockLedgerListDto> SearchAsync(StockLedgerSearchDto searchDto);

    // ── Write API (called internally by other services) ──────────────

    /// <summary>
    /// Appends a single ledger row to the current DbContext change-tracker.
    /// <para>
    /// IMPORTANT: This method does <em>not</em> call SaveChangesAsync.
    /// The caller is responsible for persisting the entry inside its own
    /// database transaction so that the ledger write is atomic with the
    /// underlying inventory change.
    /// </para>
    /// </summary>
    void WriteEntry(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks = null,
        long? createdBy = null,
        DateTime? createdAt = null);
}
