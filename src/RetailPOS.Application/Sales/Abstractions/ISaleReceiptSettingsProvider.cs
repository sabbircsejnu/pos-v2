using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Abstractions;

public interface ISaleReceiptSettingsProvider
{
    Task<SaleReceiptSettingsReadModel> GetAsync(CancellationToken cancellationToken);
}
