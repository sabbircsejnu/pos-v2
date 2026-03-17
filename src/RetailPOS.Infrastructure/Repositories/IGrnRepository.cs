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
    Task<List<PurchaseOrder>> GetPendingReceiptPOsAsync();
}
