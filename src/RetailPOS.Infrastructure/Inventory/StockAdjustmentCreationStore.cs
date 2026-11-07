using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockAdjustmentCreationStore(RetailPOSDbContext dbContext)
    : IStockAdjustmentCreationStore
{
    public async Task<long> CreateAsync(
        StockAdjustmentCreateInput input,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken)
    {
        var locationType = input.LocationType.ToLowerInvariant();
        var authorizedLocation = context.ReadScope.Locations.Any(location =>
            location.Id == input.LocationId
            && location.Type.Equals(locationType, StringComparison.OrdinalIgnoreCase));
        var locationBelongsToBusiness = locationType switch
        {
            "outlet" => await dbContext.Outlets.AnyAsync(
                outlet => outlet.Id == input.LocationId
                    && outlet.BusinessId == context.ReadScope.BusinessId,
                cancellationToken),
            "warehouse" => await dbContext.Warehouses.AnyAsync(
                warehouse => warehouse.Id == input.LocationId
                    && warehouse.BusinessId == context.ReadScope.BusinessId,
                cancellationToken),
            _ => false
        };
        if (!authorizedLocation || !locationBelongsToBusiness)
        {
            throw new UnauthorizedAccessException(
                $"You are not authorized to create adjustments for location #{input.LocationId}.");
        }

        foreach (var item in input.Items)
        {
            if (!await dbContext.ProductVariants.AnyAsync(
                    variant => variant.Id == item.VariantId,
                    cancellationToken))
            {
                throw new KeyNotFoundException($"Product variant with ID {item.VariantId} not found");
            }
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var lines = new List<StockAdjustmentLine>(input.Items.Count);
            foreach (var item in input.Items)
            {
                var inventory = await dbContext.Inventories.FirstOrDefaultAsync(row =>
                    row.VariantId == item.VariantId
                    && row.LocationId == input.LocationId
                    && row.LocationType == locationType,
                    cancellationToken);
                var currentQuantity = inventory?.Quantity ?? 0;
                var projectedQuantity = currentQuantity + item.QuantityChange;
                if (!context.AllowNegativeStock && projectedQuantity < 0)
                {
                    throw new InvalidOperationException(
                        $"Adjustment would result in negative stock for variant {item.VariantId}. Current: {currentQuantity}, Change: {item.QuantityChange}, Result: {projectedQuantity}");
                }

                lines.Add(item.ApplyUomTo(new StockAdjustmentLine
                {
                    VariantId = item.VariantId,
                    PreviousQuantity = currentQuantity,
                    QuantityChange = item.QuantityChange,
                    NewQuantity = projectedQuantity,
                    Reason = item.Reason!,
                    Notes = item.Notes,
                    CreatedAt = now,
                    UpdatedAt = now
                }));
            }

            var maxId = await dbContext.StockAdjustments
                .AsNoTracking()
                .Select(adjustment => (long?)adjustment.Id)
                .MaxAsync(cancellationToken) ?? 0;
            var initialStatus = input.SubmitForApproval
                ? StockAdjustment.StatusPendingApproval
                : StockAdjustment.StatusDraft;
            var adjustment = new StockAdjustment
            {
                AdjustmentNumber = $"ADJ-{now:yyyy}-{(maxId + 1):D5}",
                Status = initialStatus,
                LocationId = input.LocationId,
                LocationType = locationType,
                AdjustedBy = input.AdjustedBy,
                AdjustmentDate = now,
                CreatedAt = now,
                UpdatedAt = now,
                SubmittedAt = input.SubmitForApproval ? now : null,
                Lines = lines
            };

            dbContext.StockAdjustments.Add(adjustment);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return adjustment.Id;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
