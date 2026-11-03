using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Abstractions;

public interface IPendingGrnPurchaseOrderReadStore
{
    Task<IReadOnlyList<PendingGrnPurchaseOrderReadModel>> FindPendingAsync(
        long? businessId,
        CancellationToken cancellationToken);
}
