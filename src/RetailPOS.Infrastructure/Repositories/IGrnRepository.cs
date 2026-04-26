using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository interface for GRN (Goods Received Note) data access
/// </summary>
public interface IGrnRepository
{
    Task<Grn?> GetByIdAsync(long id);
    Task<List<Grn>> GetAllAsync(long? poId, string? status);
    Task<(List<Grn> Items, int TotalCount)> SearchAsync(long? poId, string? status, DateTime? startDate, DateTime? endDate, int pageNumber, int pageSize);
    Task<Grn> CreateAsync(Grn entity);
    Task<Grn> UpdateAsync(Grn entity);
    Task<bool> DeleteAsync(long id);
    /// <summary>
    /// Returns POs with status "approved" or "partial" — i.e. POs that still have
    /// outstanding quantities to receive (supports repeat/partial receipt).
    /// </summary>
    Task<List<PurchaseOrder>> GetPendingReceiptPOsAsync();
    /// <summary>
    /// Returns all GRNs (with items and variant/product nav) for a PO.
    /// Used by the cumulative variance report.
    /// </summary>
    Task<List<Grn>> GetPoGrnsWithItemsAsync(long poId);  // NEW
}
