using RetailPOS.API.Settings;

namespace RetailPOS.API.Services;

public interface ISettingsService
{
    Task<SystemSettings> GetAllSettingsAsync();
    Task<CompanySettings> GetCompanySettingsAsync();
    Task<TaxSettings> GetTaxSettingsAsync();
    Task<ReceiptSettings> GetReceiptSettingsAsync();
    Task<InventorySettings> GetInventorySettingsAsync();
    Task<SystemSettings> UpdateCompanySettingsAsync(CompanySettings settings);
    Task<SystemSettings> UpdateTaxSettingsAsync(TaxSettings settings);
    Task<SystemSettings> UpdateReceiptSettingsAsync(ReceiptSettings settings);
    Task<SystemSettings> UpdateInventorySettingsAsync(InventorySettings settings);
}
