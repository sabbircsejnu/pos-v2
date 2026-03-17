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
}
