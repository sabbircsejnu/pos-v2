namespace RetailPOS.Application.Inventory.Abstractions;

public interface IStockRequisitionLifecycleStore
{
    Task SubmitAsync(
        long id,
        long businessId,
        long submittedBy,
        CancellationToken cancellationToken);

    Task ApproveAsync(
        long id,
        long businessId,
        long approvedBy,
        CancellationToken cancellationToken);

    Task RejectAsync(
        long id,
        long businessId,
        long rejectedBy,
        string? reason,
        CancellationToken cancellationToken);
}
