namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

public interface IStockCountAdjustmentDraftStore
{
    Task GenerateAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        long actorUserId,
        CancellationToken cancellationToken);
}
