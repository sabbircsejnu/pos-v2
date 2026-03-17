using RetailPOS.API.DTOs.Customer;

namespace RetailPOS.API.Services;

public interface ICustomerService
{
    Task<CustomerDto> GetByIdAsync(long id);
    Task<CustomerListDto> SearchAsync(CustomerSearchDto searchDto);
    Task<IEnumerable<CustomerDto>> QuickSearchAsync(string query);
    Task<CustomerDto> CreateAsync(CreateCustomerDto dto);
    Task<CustomerDto> UpdateAsync(long id, UpdateCustomerDto dto);
    Task<bool> DeleteAsync(long id);
    Task<CustomerDto> AddLoyaltyPointsAsync(long id, int points);
    Task<CustomerDto> RedeemLoyaltyPointsAsync(long id, int points);
}
