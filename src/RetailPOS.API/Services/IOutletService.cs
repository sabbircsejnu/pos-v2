using RetailPOS.API.DTOs.Outlet;

namespace RetailPOS.API.Services;

public interface IOutletService
{
    Task<IEnumerable<OutletDto>> GetAllOutletsAsync();
    Task<OutletDto?> GetOutletByIdAsync(long id);
    Task<OutletDto> CreateOutletAsync(CreateOutletDto dto);
    Task<OutletDto> UpdateOutletAsync(long id, UpdateOutletDto dto);
    Task<bool> DeleteOutletAsync(long id);
    Task<IEnumerable<OutletDto>> SearchOutletsAsync(string searchTerm);
    Task<OutletStatsDto?> GetOutletStatsAsync(long id);

    // Super Admin business-scoped operations (explicit businessId, bypasses tenant context)
    Task<IEnumerable<OutletDto>> GetByBusinessIdAsync(long businessId);
    Task<OutletDto> CreateForBusinessAsync(long businessId, CreateOutletDto dto);
    Task<OutletDto> UpdateForBusinessAsync(long businessId, long id, UpdateOutletDto dto);
    Task<bool> DeleteForBusinessAsync(long businessId, long id);
}
