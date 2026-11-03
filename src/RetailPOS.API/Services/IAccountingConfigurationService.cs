using RetailPOS.API.DTOs.Accounting;

namespace RetailPOS.API.Services;

public interface IAccountingConfigurationService
{
    Task<AccountingConfigurationDto> GetBusinessConfigurationAsync();
    Task<PostingAccountResolutionDto> ResolvePostingAccountAsync(string slot, long? outletId = null);
}
