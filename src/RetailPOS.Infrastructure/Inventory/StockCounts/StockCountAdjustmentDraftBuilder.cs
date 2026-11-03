using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

/// <summary>
/// Builds the StockAdjustment draft (lines, number, provenance) from the counted differences of an approved
/// StockCount. It is the single implementation shared by the manual "generate draft" path and by the
/// asynchronous adjustment worker; it stages entities but never saves, posts or changes Inventory.
/// </summary>
public sealed class StockCountAdjustmentDraftBuilder(RetailPOSDbContext dbContext)
{
    private const string AdjustmentNumberPrefix = "ADJ";

    public static bool HasDifferences(StockCount stockCount) =>
        stockCount.Lines.Any(line => line.Difference.HasValue && line.Difference.Value != 0);

    public async Task<StockAdjustment> StageDraftAsync(
        StockCount stockCount,
        long adjustedBy,
        CancellationToken cancellationToken)
    {
        var changedLines = stockCount.Lines
            .Where(line => line.Difference.HasValue && line.Difference.Value != 0)
            .ToList();
        if (changedLines.Count == 0)
            throw new InvalidOperationException("No stock differences found to generate adjustment draft.");

        var now = DateTime.UtcNow;
        var adjustmentLines = new List<StockAdjustmentLine>(changedLines.Count);
        foreach (var line in changedLines)
        {
            var difference = line.Difference!.Value;
            if (difference != decimal.Truncate(difference))
            {
                throw new InvalidOperationException(
                    $"Difference for variant '{line.VariantName}' has fractional quantity and cannot be converted to stock adjustment draft.");
            }

            var quantityChange = decimal.ToInt32(difference);
            var inventory = await dbContext.Inventories
                .FirstOrDefaultAsync(
                    item => item.VariantId == line.VariantId
                        && item.LocationId == stockCount.LocationId
                        && item.LocationType == stockCount.LocationType,
                    cancellationToken);
            var currentQuantity = inventory?.Quantity ?? 0;
            var newQuantity = currentQuantity + quantityChange;
            if (newQuantity < 0)
            {
                throw new InvalidOperationException(
                    $"Generated adjustment would make inventory negative for variant '{line.VariantName}'.");
            }

            adjustmentLines.Add(new StockAdjustmentLine
            {
                VariantId = line.VariantId,
                PreviousQuantity = currentQuantity,
                QuantityChange = quantityChange,
                NewQuantity = newQuantity,
                Reason = "StockCountCorrection",
                Notes = $"Generated from Stock Count {stockCount.StockCountNo}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        var adjustment = new StockAdjustment
        {
            AdjustmentNumber = await GenerateAdjustmentNumberAsync(cancellationToken),
            Status = StockAdjustment.StatusDraft,
            LocationId = stockCount.LocationId,
            LocationType = stockCount.LocationType,
            SourceStockCountId = stockCount.Id,
            AdjustedBy = adjustedBy,
            AdjustmentDate = now,
            CreatedAt = now,
            UpdatedAt = now,
            Lines = adjustmentLines
        };

        dbContext.StockAdjustments.Add(adjustment);
        stockCount.Status = StockCount.StatusAdjustmentGenerated;
        return adjustment;
    }

    private async Task<string> GenerateAdjustmentNumberAsync(CancellationToken cancellationToken)
    {
        var date = DateTime.UtcNow;
        var prefix = $"{AdjustmentNumberPrefix}-{date:yyyyMMdd}";
        var todayCount = await dbContext.StockAdjustments
            .AsNoTracking()
            .CountAsync(adjustment => adjustment.AdjustmentNumber.StartsWith(prefix), cancellationToken);
        return $"{prefix}-{todayCount + 1:0000}";
    }
}
