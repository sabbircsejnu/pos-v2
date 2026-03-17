using RetailPOS.API.DTOs.Warehouse;

namespace RetailPOS.API.Services;

public interface IWarehouseService
{
    Task<IEnumerable<WarehouseDto>> GetAllWarehousesAsync();
    Task<WarehouseDto?> GetWarehouseByIdAsync(long id);
    Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseDto dto);
    Task<WarehouseDto> UpdateWarehouseAsync(long id, UpdateWarehouseDto dto);
    Task<bool> DeleteWarehouseAsync(long id);
    Task<IEnumerable<WarehouseDto>> SearchWarehousesAsync(string searchTerm);
    Task<WarehouseStatsDto?> GetWarehouseStatsAsync(long id);
}
