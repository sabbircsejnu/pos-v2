using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnPoVarianceReadStore
{
    Task<GrnPoVarianceReadModel?> FindByPurchaseOrderIdAsync(
        long purchaseOrderId,
        long? businessId,
        CancellationToken cancellationToken);
}
