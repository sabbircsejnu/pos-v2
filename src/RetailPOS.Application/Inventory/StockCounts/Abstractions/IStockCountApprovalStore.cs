namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

public interface IStockCountApprovalStore
{
    Task ApproveAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        long actorUserId,
        CancellationToken cancellationToken);
}
