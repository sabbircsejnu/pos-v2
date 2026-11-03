namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

/// <summary>
/// Read-only Mobile Stock Take history, lifecycle, reconciliation evidence and approved-result reads.
/// A null <c>ownerUserId</c> means the caller holds StockCount.ViewAll; otherwise only sessions created or
/// submitted by that user are visible. Nothing here mutates StockCount, Inventory or StockLedger.
/// </summary>
public interface IMobileStockTakeReviewStore
{
    Task<MobileStockTakeHistoryPageReadModel> SearchHistoryAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId,
        MobileStockTakeHistoryFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<MobileStockTakeLifecycleReadModel?> GetLifecycleAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId,
        long stockCountId,
        CancellationToken cancellationToken);

    Task<MobileStockTakeSubmissionPageReadModel?> GetSubmissionsAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId,
        long stockCountId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<MobileStockTakeResultsReadModel?> GetResultsAsync(
        long businessId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long? ownerUserId,
        long stockCountId,
        bool differencesOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
