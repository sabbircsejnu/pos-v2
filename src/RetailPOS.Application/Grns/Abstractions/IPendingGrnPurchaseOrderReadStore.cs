using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Abstractions;

public interface IPendingGrnPurchaseOrderReadStore
{
    Task<IReadOnlyList<PendingGrnPurchaseOrderReadModel>> FindPendingAsync(
        GrnReadScope scope,
        CancellationToken cancellationToken);
}
