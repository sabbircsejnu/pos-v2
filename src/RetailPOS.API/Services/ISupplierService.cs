using RetailPOS.API.DTOs.Supplier;

namespace RetailPOS.API.Services;

public interface ISupplierService
{
    Task<List<SupplierDto>> GetAllSuppliersAsync();
    Task<SupplierListDto> SearchSuppliersAsync(SupplierSearchDto searchDto);
    Task<SupplierDto> GetSupplierByIdAsync(long id);
    Task<SupplierDto> CreateSupplierAsync(CreateSupplierDto dto);
    Task<SupplierDto> UpdateSupplierAsync(long id, UpdateSupplierDto dto);
    Task DeleteSupplierAsync(long id);
    Task<SupplierPerformanceDto> GetSupplierPerformanceAsync(long id);
}
