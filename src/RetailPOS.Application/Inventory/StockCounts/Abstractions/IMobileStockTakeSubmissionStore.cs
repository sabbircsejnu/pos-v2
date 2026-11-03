namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

public interface IMobileStockTakeSubmissionStore
{
    /// <summary>
    /// Atomically validates the session and persists the submission job with its outbox message.
    /// Returns only after the PostgreSQL transaction commits.
    /// </summary>
    Task<AcceptMobileStockTakeSubmissionResult> AcceptAsync(
        AcceptMobileStockTakeSubmissionInput input,
        CancellationToken cancellationToken);

    Task<MobileStockTakeSubmissionReadModel?> FindAsync(
        long businessId,
        long outletId,
        IReadOnlyCollection<long> authorizedOutletIds,
        long stockCountId,
        Guid jobId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Re-evaluates the persisted initiating actor against current users, roles, permissions and Outlet access.
/// It never elevates or substitutes the actor.
/// </summary>
public interface IMobileStockTakeSubmissionActorPolicy
{
    Task<bool> IsStillAuthorizedAsync(
        MobileStockTakeSubmissionActorContext actor,
        CancellationToken cancellationToken);
}
