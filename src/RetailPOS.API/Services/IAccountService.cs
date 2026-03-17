using RetailPOS.API.DTOs.Account;

namespace RetailPOS.API.Services;

public interface IAccountService
{
    Task<AccountListDto> GetAllAsync(string? type = null);
    Task<AccountDto> GetByIdAsync(long id);
    Task<AccountDto> CreateAsync(CreateAccountDto dto);
    Task<AccountDto> UpdateAsync(long id, UpdateAccountDto dto);
    Task DeleteAsync(long id);
}
