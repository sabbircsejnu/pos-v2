namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

/// <summary>
/// Atomically approves a Submitted StockCount and persists the durable adjustment job and its transactional
/// outbox message. It never changes Inventory, StockLedger or Accounting.
/// </summary>
public interface IStockCountAdjustmentJobStore
{
    Task<ApproveStockCountWithAdjustmentResult> ApproveAndEnqueueAsync(
        ApproveStockCountWithAdjustmentInput input,
        CancellationToken cancellationToken);
}

public interface IStockCountAdjustmentProcessor
{
    /// <summary>
    /// Idempotently claims and processes one durable adjustment job. Safe under message redelivery,
    /// duplicate consumers and worker restarts.
    /// </summary>
    Task<StockCountAdjustmentProcessingOutcome> ProcessAsync(
        Guid jobId,
        long? expectedBusinessId,
        CancellationToken cancellationToken);

    /// <summary>Finds jobs whose retry is due, whose lease expired or whose message never arrived.</summary>
    Task<IReadOnlyList<Guid>> FindRecoverableJobIdsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Rechecks the persisted StockCount approver with current database state. Automatic posting requires the
/// combined StockCount.Approve and stock_adjustments.approve authorization plus access to the count location.
/// </summary>
public interface IStockCountAdjustmentApproverPolicy
{
    Task<StockCountAdjustmentApproverAuthorization?> AuthorizeAsync(
        long approverUserId,
        long businessId,
        long locationId,
        string locationType,
        CancellationToken cancellationToken);
}
