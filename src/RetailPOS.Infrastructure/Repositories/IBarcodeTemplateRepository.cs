using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Repositories;

public interface IBarcodeTemplateRepository
{
    Task<List<BarcodeTemplate>> GetByBusinessAsync(long businessId, bool includeInactive = false);
    Task<BarcodeTemplate?> GetByIdAsync(long id);
    Task<BarcodeTemplate> CreateAsync(BarcodeTemplate template);
    Task<BarcodeTemplate> UpdateAsync(BarcodeTemplate template);
    Task<bool> DeleteAsync(long id);
    Task<bool> NameExistsAsync(long businessId, string name, long? excludeId = null);
    Task ClearDefaultAsync(long businessId);
}
