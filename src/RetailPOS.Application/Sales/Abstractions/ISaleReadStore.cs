using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Abstractions;

public interface ISaleReadStore
{
    Task<SaleReadModel?> FindByIdAsync(long saleId, CancellationToken cancellationToken);

    Task<SaleReadModel?> FindForReceiptAsync(long saleId, CancellationToken cancellationToken);

    Task<SaleSearchReadModel> SearchAsync(
        SaleSearchCriteria criteria,
        CancellationToken cancellationToken);
}
