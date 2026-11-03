using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockAdjustmentCreationStore
{
    Task<long> CreateAsync(
        StockAdjustmentCreateInput input,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken);
}

public interface IStockAdjustmentCreateContextProvider
{
    Task<StockAdjustmentCreateContext> GetAsync(CancellationToken cancellationToken);
}

public interface IStockAdjustmentAuditWriter
{
    Task RecordCreatedAsync(
        StockAdjustmentReadModel adjustment,
        bool submittedForApproval,
        CancellationToken cancellationToken);

    Task RecordUpdatedAsync(
        StockAdjustmentReadModel before,
        StockAdjustmentReadModel after,
        long updatedBy,
        CancellationToken cancellationToken);

    Task RecordCancelledAsync(
        StockAdjustmentReadModel adjustment,
        string previousStatus,
        long cancelledBy,
        CancellationToken cancellationToken);

    Task RecordReversedAsync(
        StockAdjustmentReadModel adjustment,
        string reason,
        long reversedBy,
        long? originalJournalEntryId,
        long? reversalJournalEntryId,
        CancellationToken cancellationToken);

    Task RecordApprovedAsync(
        StockAdjustmentReadModel adjustment,
        long approvedBy,
        long? journalEntryId,
        CancellationToken cancellationToken);

    Task RecordSubmittedAsync(
        StockAdjustmentReadModel adjustment,
        long submittedBy,
        CancellationToken cancellationToken);

    Task RecordRejectedAsync(
        StockAdjustmentReadModel adjustment,
        long rejectedBy,
        CancellationToken cancellationToken);
}

public interface IStockAdjustmentLifecycleStore
{
    Task<long> UpdateDraftAsync(
        long adjustmentId,
        StockAdjustmentUpdateInput input,
        long updatedBy,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken);

    Task DeleteDraftAsync(
        long adjustmentId,
        long deletedBy,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken);

    Task<string> CancelAsync(
        long adjustmentId,
        long cancelledBy,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken);

    Task<long> SubmitAsync(
        long adjustmentId,
        long submittedBy,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken);

    Task<long> RejectAsync(
        long adjustmentId,
        long rejectedBy,
        string reason,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken);
}

public interface IStockAdjustmentReversalStore
{
    Task<StockAdjustmentReversalResult> ReverseAsync(
        long adjustmentId,
        long reversedBy,
        string reason,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken);
}

public interface IStockAdjustmentApprovalStore
{
    Task<StockAdjustmentApprovalResult> ApproveAsync(
        long adjustmentId,
        long approvedBy,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken);

    Task<StockAdjustmentApprovalResult> CreateAndApproveAsync(
        StockAdjustmentCreateInput input,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken);
}

public interface IStockAdjustmentPostingAccountResolver
{
    Task<long> ResolveAccountIdAsync(
        long businessId,
        string slot,
        long? outletId,
        CancellationToken cancellationToken);
}

public interface IStockAdjustmentApprovalCacheInvalidator
{
    Task InvalidateAsync(
        long locationId,
        IReadOnlyList<long> variantIds,
        CancellationToken cancellationToken);
}

public static class StockAdjustmentPostingAccountSlots
{
    public const string Inventory = "inventory";
    public const string InventoryGain = "inventory_gain";
    public const string InventoryShortage = "inventory_shortage";
}
