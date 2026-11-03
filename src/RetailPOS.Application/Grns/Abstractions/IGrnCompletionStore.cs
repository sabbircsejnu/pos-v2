using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Abstractions;

public interface IGrnCompletionStore
{
    Task<GrnCompletionScopeReadModel?> FindScopeAsync(
        long grnId,
        CancellationToken cancellationToken);

    Task<GrnReadModel> CompleteAsync(
        long grnId,
        long actorUserId,
        long? businessId,
        CancellationToken cancellationToken);
}
