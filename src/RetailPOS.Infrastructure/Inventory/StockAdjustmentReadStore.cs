using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockAdjustmentReadStore(RetailPOSDbContext dbContext)
    : IStockAdjustmentReadStore
{
    public async Task<StockAdjustmentReadModel?> GetByIdAsync(
        long id,
        StockAdjustmentReadScope scope,
        CancellationToken cancellationToken)
    {
        var adjustment = await WithDetails(ForScope(scope))
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (adjustment is null)
        {
            return null;
        }

        return (await MapAsync([adjustment], scope.BusinessId, cancellationToken))[0];
    }

    public async Task<IReadOnlyList<StockAdjustmentReadModel>> GetAllAsync(
        StockAdjustmentListFilter filter,
        StockAdjustmentReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = ForScope(scope);

        if (filter.LocationId.HasValue)
        {
            query = query.Where(row => row.LocationId == filter.LocationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.LocationType))
        {
            var locationType = filter.LocationType.ToLower();
            query = query.Where(row => row.LocationType.ToLower() == locationType);
        }

        if (filter.VariantId.HasValue)
        {
            query = query.Where(row => row.Lines.Any(line => line.VariantId == filter.VariantId.Value));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(row => row.Status == filter.Status);
        }

        var adjustments = await WithDetails(query)
            .OrderByDescending(row => row.AdjustmentDate)
            .ToListAsync(cancellationToken);

        return await MapAsync(adjustments, scope.BusinessId, cancellationToken);
    }

    public async Task<StockAdjustmentSearchReadModel> SearchAsync(
        StockAdjustmentSearchFilter filter,
        StockAdjustmentReadScope scope,
        CancellationToken cancellationToken)
    {
        var query = ForScope(scope);

        if (filter.LocationId.HasValue)
        {
            query = query.Where(row => row.LocationId == filter.LocationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.LocationType))
        {
            var locationType = filter.LocationType.ToLower();
            query = query.Where(row => row.LocationType.ToLower() == locationType);
        }

        if (filter.VariantId.HasValue)
        {
            query = query.Where(row => row.Lines.Any(line => line.VariantId == filter.VariantId.Value));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(row => row.Status == filter.Status);
        }

        if (filter.StartDate.HasValue)
        {
            query = query.Where(row => row.AdjustmentDate >= filter.StartDate.Value);
        }

        if (filter.EndDate.HasValue)
        {
            query = query.Where(row => row.AdjustmentDate <= filter.EndDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var adjustments = await WithDetails(query)
            .OrderByDescending(row => row.AdjustmentDate)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        var readModels = await MapAsync(adjustments, scope.BusinessId, cancellationToken);

        return new StockAdjustmentSearchReadModel(
            readModels,
            totalCount,
            filter.PageNumber,
            filter.PageSize);
    }

    public async Task<IReadOnlyList<StockAdjustmentReadModel>> GetHistoryAsync(
        long variantId,
        long locationId,
        StockAdjustmentReadScope scope,
        CancellationToken cancellationToken)
    {
        var adjustments = await WithDetails(ForScope(scope))
            .Where(row =>
                row.LocationId == locationId
                && row.Lines.Any(line => line.VariantId == variantId))
            .OrderByDescending(row => row.AdjustmentDate)
            .ToListAsync(cancellationToken);

        return await MapAsync(adjustments, scope.BusinessId, cancellationToken);
    }

    private IQueryable<StockAdjustment> ForScope(StockAdjustmentReadScope scope)
    {
        var outletIds = scope.Locations
            .Where(location => location.Type.Equals("outlet", StringComparison.OrdinalIgnoreCase))
            .Select(location => location.Id)
            .Distinct()
            .ToList();
        var warehouseIds = scope.Locations
            .Where(location => location.Type.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
            .Select(location => location.Id)
            .Distinct()
            .ToList();

        return dbContext.StockAdjustments
            .AsNoTracking()
            .Where(adjustment =>
                (adjustment.LocationType.ToLower() == "outlet"
                    && outletIds.Contains(adjustment.LocationId)
                    && dbContext.Outlets.Any(outlet =>
                        outlet.Id == adjustment.LocationId
                        && outlet.BusinessId == scope.BusinessId))
                || (adjustment.LocationType.ToLower() == "warehouse"
                    && warehouseIds.Contains(adjustment.LocationId)
                    && dbContext.Warehouses.Any(warehouse =>
                        warehouse.Id == adjustment.LocationId
                        && warehouse.BusinessId == scope.BusinessId)));
    }

    private static IQueryable<StockAdjustment> WithDetails(IQueryable<StockAdjustment> query) =>
        query
            .Include(adjustment => adjustment.Lines)
                .ThenInclude(line => line.Variant)
                    .ThenInclude(variant => variant.Product)
            .Include(adjustment => adjustment.Adjuster)
            .Include(adjustment => adjustment.Approver)
            .Include(adjustment => adjustment.Rejector)
            .Include(adjustment => adjustment.Canceller);

    private async Task<IReadOnlyList<StockAdjustmentReadModel>> MapAsync(
        IReadOnlyList<StockAdjustment> adjustments,
        long businessId,
        CancellationToken cancellationToken)
    {
        var outletIds = adjustments
            .Where(adjustment => adjustment.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase))
            .Select(adjustment => adjustment.LocationId)
            .Distinct()
            .ToList();
        var warehouseIds = adjustments
            .Where(adjustment => adjustment.LocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
            .Select(adjustment => adjustment.LocationId)
            .Distinct()
            .ToList();
        var outletNames = await dbContext.Outlets
            .AsNoTracking()
            .Where(outlet => outlet.BusinessId == businessId && outletIds.Contains(outlet.Id))
            .ToDictionaryAsync(outlet => outlet.Id, outlet => outlet.Name, cancellationToken);
        var warehouseNames = await dbContext.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.BusinessId == businessId && warehouseIds.Contains(warehouse.Id))
            .ToDictionaryAsync(warehouse => warehouse.Id, warehouse => warehouse.Name, cancellationToken);

        return adjustments.Select(adjustment =>
        {
            var lines = adjustment.Lines
                .OrderBy(line => line.Id)
                .Select(line => new StockAdjustmentLineReadModel(
                    line.Id,
                    line.VariantId,
                    line.Variant?.Product?.Name ?? string.Empty,
                    line.Variant?.Sku ?? string.Empty,
                    line.Variant?.Product?.ProductCode ?? string.Empty,
                    line.Variant?.Barcode ?? line.Variant?.Product?.Barcode ?? string.Empty,
                    line.PreviousQuantity,
                    line.QuantityChange,
                    line.NewQuantity,
                    line.Reason,
                    line.Notes))
                .ToArray();
            var firstLine = lines.FirstOrDefault();
            var totalIncrease = lines
                .Where(line => line.QuantityChange > 0)
                .Sum(line => line.QuantityChange);
            var totalDecrease = lines
                .Where(line => line.QuantityChange < 0)
                .Sum(line => Math.Abs(line.QuantityChange));
            var locationName = adjustment.LocationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
                ? outletNames.GetValueOrDefault(adjustment.LocationId, $"Outlet {adjustment.LocationId}")
                : adjustment.LocationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase)
                    ? warehouseNames.GetValueOrDefault(adjustment.LocationId, $"Warehouse {adjustment.LocationId}")
                    : $"Location {adjustment.LocationId}";

            return new StockAdjustmentReadModel(
                adjustment.Id,
                adjustment.AdjustmentNumber,
                adjustment.Status,
                adjustment.LocationId,
                adjustment.LocationType,
                adjustment.SourceStockCountId,
                locationName,
                firstLine?.VariantId ?? 0,
                firstLine?.VariantSku ?? string.Empty,
                firstLine?.ProductName ?? string.Empty,
                firstLine?.PreviousQuantity ?? 0,
                firstLine?.QuantityChange ?? 0,
                firstLine?.NewQuantity ?? 0,
                firstLine?.Reason ?? string.Empty,
                firstLine?.Notes,
                lines.Length,
                totalIncrease,
                totalDecrease,
                totalIncrease - totalDecrease,
                lines,
                adjustment.AdjustedBy,
                adjustment.Adjuster?.Name ?? string.Empty,
                adjustment.AdjustmentDate,
                adjustment.CreatedAt,
                adjustment.UpdatedAt,
                adjustment.SubmittedAt,
                adjustment.ApprovedBy,
                adjustment.Approver?.Name,
                adjustment.ApprovedAt,
                adjustment.RejectedBy,
                adjustment.Rejector?.Name,
                adjustment.RejectedAt,
                adjustment.RejectionReason,
                adjustment.CancelledBy,
                adjustment.Canceller?.Name,
                adjustment.CancelledAt);
        }).ToArray();
    }
}
