using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnPoVarianceReadStore
{
    Task<GrnPoVarianceReadModel?> FindByPurchaseOrderIdAsync(
        long purchaseOrderId,
        GrnReadScope scope,
        CancellationToken cancellationToken);
}
