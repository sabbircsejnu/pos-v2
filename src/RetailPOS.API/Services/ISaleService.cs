using RetailPOS.API.DTOs.Sale;

namespace RetailPOS.API.Services;

public interface ISaleService
{
    Task<SaleDto> GetByIdAsync(long id);
    Task<SaleListDto> SearchAsync(SaleSearchDto searchDto);
    Task<SaleDto> CreateAsync(CreateSaleDto dto);
    Task<SaleDto> VoidAsync(long id, VoidSaleDto dto);
    Task<SaleDto> RefundAsync(long id, RefundSaleDto dto);
    Task<SaleSummaryDto> GetTodaysSummaryAsync(long? outletId = null);
    Task<SaleDto> GetReceiptAsync(long id);
}
