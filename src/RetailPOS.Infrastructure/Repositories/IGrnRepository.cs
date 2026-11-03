using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

/// <summary>
/// Repository interface for GRN (Goods Received Note) data access
/// </summary>
public interface IGrnRepository
{
    Task<Grn?> GetByIdAsync(long id);
    Task<Grn> UpdateAsync(Grn entity);
    Task<bool> DeleteAsync(long id);
}
