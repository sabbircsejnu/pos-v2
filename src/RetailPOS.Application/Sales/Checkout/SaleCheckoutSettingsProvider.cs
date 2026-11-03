using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Settings.Abstractions;

namespace RetailPOS.Application.Sales.Checkout;

public sealed class SaleCheckoutSettingsProvider(IBusinessPosSettingsStore settingsStore) : ISaleCheckoutSettingsProvider
{
    public async Task<SaleTaxPolicy> GetTaxPolicyAsync(long businessId, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(businessId, cancellationToken);
        return new SaleTaxPolicy(settings.TaxEnabled, settings.DefaultTaxRate, settings.PricesIncludeTax);
    }

    public async Task<SaleDiscountPolicy> GetDiscountPolicyAsync(long businessId, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(businessId, cancellationToken);
        return new SaleDiscountPolicy(settings.MaxManualDiscountPercent);
    }
}
