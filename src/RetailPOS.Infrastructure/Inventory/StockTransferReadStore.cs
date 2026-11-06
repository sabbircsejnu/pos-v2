using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Queries;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockTransferReadStore(RetailPOSDbContext dbContext)
    : IStockTransferReadStore
{
    public async Task<StockTransferReadModel?> GetByIdAsync(
        long id,
        long businessId,
        CancellationToken cancellationToken)
    {
        var scope = new StockTransferReadScope(businessId, true, null, null);
        var transfer = await ApplyBusinessAndVisibilityScope(CreateReadQuery(), scope)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (transfer is null)
            return null;

        return (await MapAsync([transfer], cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<StockTransferReadModel>> GetAllAsync(
        StockTransferListFilter filter,
        StockTransferReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = ApplyBusinessAndVisibilityScope(
            CreateReadQuery(),
            scope);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var lowerStatus = filter.Status.ToLower();
            query = query.Where(transfer => transfer.Status.ToLower() == lowerStatus);
        }

        if (filter.FromLocationId.HasValue)
            query = query.Where(transfer => transfer.FromLocationId == filter.FromLocationId.Value);

        if (filter.ToLocationId.HasValue)
            query = query.Where(transfer => transfer.ToLocationId == filter.ToLocationId.Value);

        var transfers = await query
            .OrderByDescending(transfer => transfer.CreatedAt)
            .ThenByDescending(transfer => transfer.Id)
            .ToListAsync(cancellationToken);
        return await MapAsync(transfers, cancellationToken);
    }

    public async Task<StockTransferSearchResult> SearchAsync(
        StockTransferSearchFilter filter,
        StockTransferReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = ApplyBusinessAndVisibilityScope(
            CreateReadQuery(),
            scope);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var lowerStatus = filter.Status.ToLower();
            query = query.Where(transfer => transfer.Status.ToLower() == lowerStatus);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var trimmed = filter.SearchTerm.Trim();
            query = query.Where(transfer =>
                (transfer.TransferNo ?? string.Empty).Contains(trimmed));
        }

        if (filter.FromLocationId.HasValue)
            query = query.Where(transfer => transfer.FromLocationId == filter.FromLocationId.Value);

        if (!string.IsNullOrWhiteSpace(filter.FromLocationType))
        {
            var lowerType = filter.FromLocationType.ToLower();
            query = query.Where(transfer => transfer.FromLocationType.ToLower() == lowerType);
        }

        if (filter.ToLocationId.HasValue)
            query = query.Where(transfer => transfer.ToLocationId == filter.ToLocationId.Value);

        if (!string.IsNullOrWhiteSpace(filter.ToLocationType))
        {
            var lowerType = filter.ToLocationType.ToLower();
            query = query.Where(transfer => transfer.ToLocationType.ToLower() == lowerType);
        }

        if (filter.ProductVariantId.HasValue)
        {
            query = query.Where(transfer => transfer.Items.Any(item =>
                item.VariantId == filter.ProductVariantId.Value));
        }

        if (!string.IsNullOrWhiteSpace(filter.TransferType))
        {
            var lowerTransferType = filter.TransferType.ToLower();
            query = query.Where(transfer =>
                transfer.TransferType.ToLower() == lowerTransferType);
        }

        if (filter.DateFrom.HasValue)
        {
            var fromDate = filter.DateFrom.Value.Date;
            query = query.Where(transfer => transfer.TransferDate >= fromDate);
        }

        if (filter.DateTo.HasValue)
        {
            var toDateExclusive = filter.DateTo.Value.Date.AddDays(1);
            query = query.Where(transfer => transfer.TransferDate < toDateExclusive);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var transfers = await query
            .OrderByDescending(transfer => transfer.Id)
            .ThenByDescending(transfer => transfer.Id)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new StockTransferSearchResult(
            await MapAsync(transfers, cancellationToken),
            totalCount,
            filter.PageNumber,
            filter.PageSize);
    }

    private IQueryable<StockTransfer> CreateReadQuery() =>
        dbContext.StockTransfers
            .AsNoTracking()
            .Include(transfer => transfer.Approver)
            .Include(transfer => transfer.Creator)
            .Include(transfer => transfer.Items)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
                        .ThenInclude(product => product.Images.Where(image => image.IsPrimary));

    private IQueryable<StockTransfer> ApplyBusinessAndVisibilityScope(
        IQueryable<StockTransfer> query,
        StockTransferReadScope scope)
    {
        query = query.Where(transfer =>
            ((transfer.FromLocationType.ToLower() == "outlet"
                && dbContext.Outlets.Any(location =>
                    location.Id == transfer.FromLocationId
                    && location.BusinessId == scope.BusinessId))
             || (transfer.FromLocationType.ToLower() == "warehouse"
                && dbContext.Warehouses.Any(location =>
                    location.Id == transfer.FromLocationId
                    && location.BusinessId == scope.BusinessId)))
            && ((transfer.ToLocationType.ToLower() == "outlet"
                && dbContext.Outlets.Any(location =>
                    location.Id == transfer.ToLocationId
                    && location.BusinessId == scope.BusinessId))
             || (transfer.ToLocationType.ToLower() == "warehouse"
                && dbContext.Warehouses.Any(location =>
                    location.Id == transfer.ToLocationId
                    && location.BusinessId == scope.BusinessId))));

        return query.ApplyVisibility(
            scope.CanViewAllLocations,
            scope.DefaultLocationType,
            scope.DefaultLocationId);
    }

    private async Task<IReadOnlyList<StockTransferReadModel>> MapAsync(
        IReadOnlyList<StockTransfer> transfers,
        CancellationToken cancellationToken)
    {
        var transferIds = transfers.Select(transfer => transfer.Id).ToList();
        var systemReturns = await dbContext.StockTransfers
            .AsNoTracking()
            .Include(transfer => transfer.Items)
            .Where(transfer => transfer.OriginalTransferId != null
                && transferIds.Contains(transfer.OriginalTransferId.Value))
            .ToDictionaryAsync(transfer => transfer.OriginalTransferId!.Value, cancellationToken);

        var result = new List<StockTransferReadModel>(transfers.Count);
        foreach (var transfer in transfers)
        {
            systemReturns.TryGetValue(transfer.Id, out var systemReturn);
            var isInTransit = transfer.Status.Equals(StockTransfer.StatusInTransit, StringComparison.OrdinalIgnoreCase);
            var returnInTransit = systemReturn is not null
                && systemReturn.Status.Equals(StockTransfer.StatusInTransit, StringComparison.OrdinalIgnoreCase);
            result.Add(new StockTransferReadModel(
                transfer.Id,
                transfer.TransferNo,
                transfer.TransferType,
                transfer.RelatedRequisitionId,
                transfer.FromLocationId,
                transfer.FromLocationType,
                await ResolveLocationNameAsync(
                    transfer.FromLocationId,
                    transfer.FromLocationType,
                    cancellationToken),
                transfer.ToLocationId,
                transfer.ToLocationType,
                await ResolveLocationNameAsync(
                    transfer.ToLocationId,
                    transfer.ToLocationType,
                    cancellationToken),
                transfer.TransferDate,
                transfer.Status,
                transfer.Notes,
                transfer.ApprovedBy,
                transfer.Approver?.Name,
                transfer.ApprovedAt,
                transfer.SubmittedBy,
                transfer.SubmittedAt,
                transfer.DispatchedBy,
                transfer.DispatchedAt,
                transfer.ReceivedBy,
                transfer.ReceivedAt,
                transfer.RejectedBy,
                transfer.RejectedAt,
                transfer.CancelledBy,
                transfer.CancelledAt,
                transfer.UpdatedBy,
                transfer.UpdatedAt,
                transfer.CreatedBy,
                transfer.Creator?.Name,
                transfer.CreatedAt,
                transfer.Items.Select(item => new StockTransferItemReadModel(
                    item.Id,
                    item.VariantId,
                    item.Variant?.Product?.ProductCode,
                    item.Variant?.Sku ?? string.Empty,
                    item.Variant?.Product?.Name ?? string.Empty,
                    item.Variant?.Attributes,
                    item.Variant?.Product?.Images.FirstOrDefault(image => image.IsPrimary)?.ThumbPath,
                    ResolveTransferQuantity(item),
                    item.RequestedQuantity,
                    ResolveTransferQuantity(item),
                    item.AcceptedQuantity,
                    item.RejectedQuantity,
                    item.UnitCost,
                    item.Remarks,
                    item.MissingQuantity,
                    item.DamagedQuantity,
                    isInTransit && !transfer.OriginalTransferId.HasValue ? ResolveTransferQuantity(item) : 0,
                    isInTransit && transfer.OriginalTransferId.HasValue
                        ? ResolveTransferQuantity(item)
                        : returnInTransit
                            ? systemReturn!.Items
                                .Where(line => line.VariantId == item.VariantId)
                                .Sum(ResolveTransferQuantity)
                            : 0)).ToArray(),
                transfer.OriginalTransferId,
                systemReturn?.Id,
                systemReturn?.TransferNo));
        }

        return result;
    }

    private async Task<string> ResolveLocationNameAsync(
        long locationId,
        string locationType,
        CancellationToken cancellationToken)
    {
        if (locationType.ToLower() == "outlet")
        {
            var name = await dbContext.Outlets
                .Where(location => location.Id == locationId)
                .Select(location => location.Name)
                .FirstOrDefaultAsync(cancellationToken);
            return name ?? $"Outlet {locationId}";
        }

        if (locationType.ToLower() == "warehouse")
        {
            var name = await dbContext.Warehouses
                .Where(location => location.Id == locationId)
                .Select(location => location.Name)
                .FirstOrDefaultAsync(cancellationToken);
            return name ?? $"Warehouse {locationId}";
        }

        return $"Location {locationId}";
    }

    private static int ResolveTransferQuantity(StockTransferItem item) =>
        item.TransferQuantity > 0 ? item.TransferQuantity : item.Quantity;
}
