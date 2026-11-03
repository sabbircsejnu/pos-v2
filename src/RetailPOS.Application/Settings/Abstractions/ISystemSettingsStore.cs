using RetailPOS.Application.Settings;

namespace RetailPOS.Application.Settings.Abstractions;

public interface ISystemSettingsStore
{
    Task<SystemSettings> GetAsync(CancellationToken cancellationToken);
    Task<SystemSettings> UpdateCompanyAsync(
        CompanySettings settings,
        CancellationToken cancellationToken);
    Task<SystemSettings> UpdateCurrencyAsync(
        CurrencySettings settings,
        CancellationToken cancellationToken);
    Task<SystemSettings> UpdateTaxAsync(
        TaxSettings settings,
        CancellationToken cancellationToken);
    Task<SystemSettings> UpdateReceiptAsync(
        ReceiptSettings settings,
        CancellationToken cancellationToken);
    Task<SystemSettings> UpdateInvoiceNumberAsync(
        InvoiceNumberSettings settings,
        CancellationToken cancellationToken);
    Task<SystemSettings> UpdateInventoryAsync(
        InventorySettings settings,
        CancellationToken cancellationToken);
    Task<string> AllocateNextInvoiceNumberAsync(DateTime utcNow, CancellationToken cancellationToken);
}
