using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockAdjustmentLifecycleStore(
    RetailPOSDbContext dbContext,
    ILogger<StockAdjustmentLifecycleStore> logger)
    : IStockAdjustmentLifecycleStore
{
    public async Task<long> UpdateDraftAsync(
        long adjustmentId,
        StockAdjustmentUpdateInput input,
        long updatedBy,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken)
    {
        var adjustment = await GetTrackedAdjustmentAsync(
            adjustmentId,
            includeLines: true,
            cancellationToken);
        await StockAdjustmentAccessGuard.EnsureAuthorizedLocationAsync(
            dbContext,
            adjustment.LocationId,
            adjustment.LocationType,
            context,
            "edit",
            cancellationToken);
        EnsureStatus(adjustment, StockAdjustment.StatusDraft, "edit");
        await StockAdjustmentAccessGuard.EnsureAuthorizedLocationAsync(
            dbContext,
            input.LocationId,
            input.LocationType,
            context,
            "edit",
            cancellationToken);

        var locationType = input.LocationType.ToLowerInvariant();
        var replacementLines = new List<StockAdjustmentLine>(input.Items.Count);
        foreach (var item in input.Items)
        {
            if (!await dbContext.ProductVariants.AnyAsync(
                    variant => variant.Id == item.VariantId,
                    cancellationToken))
            {
                throw new KeyNotFoundException(
                    $"Product variant with ID {item.VariantId} not found");
            }
        }

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

            replacementLines.Add(item.ApplyUomTo(new StockAdjustmentLine
            {
                VariantId = item.VariantId,
                PreviousQuantity = currentQuantity,
                QuantityChange = item.QuantityChange,
                NewQuantity = projectedQuantity,
                Reason = item.Reason!,
                Notes = item.Notes,
                CreatedAt = adjustment.CreatedAt,
                UpdatedAt = DateTime.UtcNow
            }));
        }

        var now = DateTime.UtcNow;
        adjustment.LocationId = input.LocationId;
        adjustment.LocationType = locationType;
        adjustment.UpdatedAt = now;
        adjustment.AdjustmentDate = now;
        dbContext.StockAdjustmentLines.RemoveRange(adjustment.Lines);
        adjustment.Lines = replacementLines;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Stock adjustment {AdjustmentNumber} updated by user {UserId}",
            adjustment.AdjustmentNumber,
            updatedBy);
        return adjustment.Id;
    }

    public async Task DeleteDraftAsync(
        long adjustmentId,
        long deletedBy,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken)
    {
        var adjustment = await GetTrackedAdjustmentAsync(
            adjustmentId,
            includeLines: true,
            cancellationToken);
        await StockAdjustmentAccessGuard.EnsureAuthorizedLocationAsync(
            dbContext,
            adjustment.LocationId,
            adjustment.LocationType,
            context,
            "delete",
            cancellationToken);
        EnsureStatus(adjustment, StockAdjustment.StatusDraft, "delete");

        dbContext.StockAdjustments.Remove(adjustment);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Stock adjustment {AdjustmentNumber} deleted by user {UserId}",
            adjustment.AdjustmentNumber,
            deletedBy);
    }

    public async Task<string> CancelAsync(
        long adjustmentId,
        long cancelledBy,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken)
    {
        var adjustment = await GetTrackedAdjustmentAsync(
            adjustmentId,
            cancellationToken);
        await StockAdjustmentAccessGuard.EnsureAuthorizedLocationAsync(
            dbContext,
            adjustment.LocationId,
            adjustment.LocationType,
            context,
            "cancel",
            cancellationToken);
        if (adjustment.Status != StockAdjustment.StatusDraft
            && adjustment.Status != StockAdjustment.StatusPendingApproval)
        {
            throw new InvalidOperationException(
                $"Only {StockAdjustment.StatusDraft} or {StockAdjustment.StatusPendingApproval} adjustments can be cancelled.");
        }

        var previousStatus = adjustment.Status;
        var now = DateTime.UtcNow;
        adjustment.Status = StockAdjustment.StatusCancelled;
        adjustment.CancelledBy = cancelledBy;
        adjustment.CancelledAt = now;
        adjustment.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Stock adjustment {AdjustmentNumber} cancelled by user {UserId}",
            adjustment.AdjustmentNumber,
            cancelledBy);
        return previousStatus;
    }

    public async Task<long> SubmitAsync(
        long adjustmentId,
        long submittedBy,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken)
    {
        var adjustment = await GetTrackedAdjustmentAsync(
            adjustmentId,
            cancellationToken);
        await StockAdjustmentAccessGuard.EnsureAuthorizedLocationAsync(
            dbContext,
            adjustment.LocationId,
            adjustment.LocationType,
            context,
            "submit",
            cancellationToken);
        EnsureStatus(adjustment, StockAdjustment.StatusDraft, "submit");

        var now = DateTime.UtcNow;
        adjustment.Status = StockAdjustment.StatusPendingApproval;
        adjustment.SubmittedAt = now;
        adjustment.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return adjustment.Id;
    }

    public async Task<long> RejectAsync(
        long adjustmentId,
        long rejectedBy,
        string reason,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken)
    {
        var adjustment = await GetTrackedAdjustmentAsync(
            adjustmentId,
            cancellationToken);
        await StockAdjustmentAccessGuard.EnsureAuthorizedLocationAsync(
            dbContext,
            adjustment.LocationId,
            adjustment.LocationType,
            context,
            "reject",
            cancellationToken);
        EnsureStatus(adjustment, StockAdjustment.StatusPendingApproval, "reject");

        var now = DateTime.UtcNow;
        adjustment.Status = StockAdjustment.StatusRejected;
        adjustment.RejectedBy = rejectedBy;
        adjustment.RejectedAt = now;
        adjustment.RejectionReason = reason.Trim();
        adjustment.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return adjustment.Id;
    }

    private async Task<StockAdjustment> GetTrackedAdjustmentAsync(
        long adjustmentId,
        CancellationToken cancellationToken) =>
        await GetTrackedAdjustmentAsync(
            adjustmentId,
            includeLines: false,
            cancellationToken);

    private async Task<StockAdjustment> GetTrackedAdjustmentAsync(
        long adjustmentId,
        bool includeLines,
        CancellationToken cancellationToken)
    {
        IQueryable<StockAdjustment> query = dbContext.StockAdjustments;
        if (includeLines)
        {
            query = query.Include(row => row.Lines);
        }

        return await query.FirstOrDefaultAsync(
                row => row.Id == adjustmentId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock adjustment with ID {adjustmentId} not found");
    }

    private static void EnsureStatus(
        StockAdjustment adjustment,
        string expectedStatus,
        string action)
    {
        if (!string.Equals(adjustment.Status, expectedStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Only {expectedStatus} adjustments can be {action}d. Current status: {adjustment.Status}.");
        }
    }
}
