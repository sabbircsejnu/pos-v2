namespace RetailPOS.Application.Inventory.StockCounts.Abstractions;

public interface IStockCountLifecycleStore
{
    Task SubmitAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        long actorUserId,
        CancellationToken cancellationToken);

    Task RejectAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        long actorUserId,
        string? reason,
        CancellationToken cancellationToken);

    Task ReopenAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        long actorUserId,
        CancellationToken cancellationToken);
}
