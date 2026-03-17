using RetailPOS.API.DTOs.Bill;

namespace RetailPOS.API.Services;

public interface IBillService
{
    Task<BillListDto> SearchAsync(BillSearchDto searchDto);
    Task<BillDto> GetByIdAsync(long id);
    Task<BillDto> CreateAsync(CreateBillDto dto);
    Task<BillDto> UpdateStatusAsync(long id, UpdateBillStatusDto dto);
    Task<BillSummaryDto> GetSummaryAsync();
}
