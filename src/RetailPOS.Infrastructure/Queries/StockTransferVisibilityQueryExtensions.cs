using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Queries;

public static class StockTransferVisibilityQueryExtensions
{
    public static IQueryable<StockTransfer> ApplyVisibility(
        this IQueryable<StockTransfer> query,
        bool canViewAllLocations,
        string? defaultLocationType,
        long? defaultLocationId)
    {
        if (canViewAllLocations)
        {
            return query;
        }

        if (!defaultLocationId.HasValue || string.IsNullOrWhiteSpace(defaultLocationType))
        {
            return query.Where(_ => false);
        }

        var normalizedType = defaultLocationType.Trim().ToLowerInvariant();
        if (normalizedType != "outlet" && normalizedType != "warehouse")
        {
            return query.Where(_ => false);
        }

        var locationId = defaultLocationId.Value;
        return query.Where(transfer =>
            (transfer.FromLocationType.ToLower() == normalizedType && transfer.FromLocationId == locationId)
            ||
            (transfer.ToLocationType.ToLower() == normalizedType && transfer.ToLocationId == locationId));
    }
}