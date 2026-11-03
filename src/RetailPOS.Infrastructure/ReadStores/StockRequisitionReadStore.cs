using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockRequisitions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockRequisitionReadStore(RetailPOSDbContext dbContext)
    : IStockRequisitionReadStore
{
    public async Task<StockRequisitionReadModel?> GetByIdAsync(
        long id,
        long businessId,
        CancellationToken cancellationToken)
    {
        var requisition = await InBusiness(CreateReadQuery(), businessId)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (requisition is null)
            return null;

        var names = await ResolveLocationNamesAsync([requisition], cancellationToken);
        return Map(requisition, names);
    }

    public async Task<IReadOnlyList<StockRequisitionReadModel>> GetAllAsync(
        StockRequisitionFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(InBusiness(CreateReadQuery(), businessId), filter);
        var requisitions = await query
            .OrderByDescending(candidate => candidate.RequestDate)
            .ToListAsync(cancellationToken);
        var names = await ResolveLocationNamesAsync(requisitions, cancellationToken);
        return requisitions.Select(candidate => Map(candidate, names)).ToArray();
    }

    public async Task<StockRequisitionSearchResult> SearchAsync(
        StockRequisitionFilter filter,
        long businessId,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(InBusiness(CreateReadQuery(), businessId), filter);
        var totalCount = await query.CountAsync(cancellationToken);
        var requisitions = await query
            .OrderByDescending(candidate => candidate.RequestDate)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        var names = await ResolveLocationNamesAsync(requisitions, cancellationToken);
        return new StockRequisitionSearchResult(
            requisitions.Select(candidate => Map(candidate, names)).ToArray(),
            totalCount,
            filter.PageNumber,
            filter.PageSize);
    }

    private IQueryable<StockRequisition> CreateReadQuery() =>
        dbContext.StockRequisitions
            .AsNoTracking()
            .Include(candidate => candidate.Requester)
            .Include(candidate => candidate.Lines)
                .ThenInclude(line => line.Variant)
                    .ThenInclude(variant => variant.Product)
                        .ThenInclude(product => product.Images.Where(image => image.IsPrimary));

    private IQueryable<StockRequisition> InBusiness(
        IQueryable<StockRequisition> query,
        long businessId) =>
        query.Where(requisition =>
            ((requisition.RequestingLocationType.ToLower() == "outlet"
                && dbContext.Outlets.Any(location =>
                    location.Id == requisition.RequestingLocationId
                    && location.BusinessId == businessId))
             || (requisition.RequestingLocationType.ToLower() == "warehouse"
                && dbContext.Warehouses.Any(location =>
                    location.Id == requisition.RequestingLocationId
                    && location.BusinessId == businessId)))
            && ((requisition.SourceLocationType.ToLower() == "outlet"
                && dbContext.Outlets.Any(location =>
                    location.Id == requisition.SourceLocationId
                    && location.BusinessId == businessId))
             || (requisition.SourceLocationType.ToLower() == "warehouse"
                && dbContext.Warehouses.Any(location =>
                    location.Id == requisition.SourceLocationId
                    && location.BusinessId == businessId))));

    private static IQueryable<StockRequisition> ApplyFilters(
        IQueryable<StockRequisition> query,
        StockRequisitionFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.ToLowerInvariant();
            query = query.Where(requisition => requisition.Status.ToLower() == status);
        }

        if (filter.RequestingLocationId.HasValue)
            query = query.Where(requisition =>
                requisition.RequestingLocationId == filter.RequestingLocationId.Value);

        if (!string.IsNullOrWhiteSpace(filter.RequestingLocationType))
        {
            var locationType = filter.RequestingLocationType.ToLowerInvariant();
            query = query.Where(requisition =>
                requisition.RequestingLocationType.ToLower() == locationType);
        }

        if (filter.SourceLocationId.HasValue)
            query = query.Where(requisition =>
                requisition.SourceLocationId == filter.SourceLocationId.Value);

        if (!string.IsNullOrWhiteSpace(filter.SourceLocationType))
        {
            var locationType = filter.SourceLocationType.ToLowerInvariant();
            query = query.Where(requisition =>
                requisition.SourceLocationType.ToLower() == locationType);
        }

        return query;
    }

    private async Task<IReadOnlyDictionary<(string Type, long Id), string>> ResolveLocationNamesAsync(
        IReadOnlyCollection<StockRequisition> requisitions,
        CancellationToken cancellationToken)
    {
        var outletIds = requisitions
            .Where(candidate => candidate.RequestingLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
                || candidate.SourceLocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
            .SelectMany(candidate => new[] { candidate.RequestingLocationId, candidate.SourceLocationId })
            .Distinct()
            .ToList();
        var warehouseIds = requisitions
            .Where(candidate => candidate.RequestingLocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase)
                || candidate.SourceLocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
            .SelectMany(candidate => new[] { candidate.RequestingLocationId, candidate.SourceLocationId })
            .Distinct()
            .ToList();

        var names = new Dictionary<(string Type, long Id), string>();
        foreach (var outlet in await dbContext.Outlets.AsNoTracking()
                     .Where(candidate => outletIds.Contains(candidate.Id))
                     .Select(candidate => new { candidate.Id, candidate.Name })
                     .ToListAsync(cancellationToken))
        {
            names[("outlet", outlet.Id)] = outlet.Name;
        }

        foreach (var warehouse in await dbContext.Warehouses.AsNoTracking()
                     .Where(candidate => warehouseIds.Contains(candidate.Id))
                     .Select(candidate => new { candidate.Id, candidate.Name })
                     .ToListAsync(cancellationToken))
        {
            names[("warehouse", warehouse.Id)] = warehouse.Name;
        }

        return names;
    }

    private static StockRequisitionReadModel Map(
        StockRequisition requisition,
        IReadOnlyDictionary<(string Type, long Id), string> locationNames) =>
        new(
            requisition.Id,
            requisition.RequisitionNo,
            requisition.RequestingLocationId,
            requisition.RequestingLocationType,
            ResolveLocationName(
                locationNames,
                requisition.RequestingLocationId,
                requisition.RequestingLocationType),
            requisition.SourceLocationId,
            requisition.SourceLocationType,
            ResolveLocationName(
                locationNames,
                requisition.SourceLocationId,
                requisition.SourceLocationType),
            requisition.RequestedBy,
            requisition.Requester?.Name,
            requisition.RequestDate,
            requisition.Status,
            requisition.Notes,
            requisition.CreatedAt,
            requisition.UpdatedAt,
            requisition.UpdatedBy,
            requisition.SubmittedAt,
            requisition.SubmittedBy,
            requisition.ApprovedAt,
            requisition.ApprovedBy,
            requisition.RejectedAt,
            requisition.RejectedBy,
            requisition.RejectionReason,
            requisition.ClosedAt,
            requisition.ClosedBy,
            requisition.Lines.Select(line => new StockRequisitionLineReadModel(
                line.Id,
                line.VariantId,
                line.Variant?.Product?.ProductCode,
                line.Variant?.Sku ?? string.Empty,
                line.Variant?.Product?.Name ?? string.Empty,
                line.Variant?.Attributes,
                line.Variant?.Product?.Images.FirstOrDefault(image => image.IsPrimary)?.ThumbPath,
                line.RequestedQuantity,
                line.FulfilledQuantity,
                line.Remarks)).ToArray());

    private static string ResolveLocationName(
        IReadOnlyDictionary<(string Type, long Id), string> locationNames,
        long locationId,
        string locationType)
    {
        var normalizedType = locationType.ToLowerInvariant();
        if (locationNames.TryGetValue((normalizedType, locationId), out var name))
            return name;

        return normalizedType switch
        {
            "outlet" => $"Outlet {locationId}",
            "warehouse" => $"Warehouse {locationId}",
            _ => $"Location {locationId}"
        };
    }
}
