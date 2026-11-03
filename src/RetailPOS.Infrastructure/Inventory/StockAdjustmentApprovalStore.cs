using MediatR;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockAdjustmentApprovalStore(
    RetailPOSDbContext dbContext,
    IStockLedgerWriter stockLedger,
    IStockAdjustmentPostingAccountResolver postingAccountResolver,
    ISender sender)
    : IStockAdjustmentApprovalStore
{
    public async Task<StockAdjustmentApprovalResult> ApproveAsync(
        long adjustmentId,
        long approvedBy,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken)
    {
        var adjustment = await dbContext.StockAdjustments
            .Include(row => row.Lines)
            .FirstOrDefaultAsync(row => row.Id == adjustmentId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock adjustment with ID {adjustmentId} not found");
        await StockAdjustmentAccessGuard.EnsureAuthorizedLocationAsync(
            dbContext,
            adjustment.LocationId,
            adjustment.LocationType,
            context,
            "approve",
            cancellationToken);
        EnsurePendingApproval(adjustment);

        // A caller-owned transaction (the asynchronous StockCount adjustment worker's audited boundary)
        // is joined so posting and the caller's durable outcome commit or roll back together.
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var now = DateTime.UtcNow;
            var journalEntryId = await ApplyApprovalAsync(
                adjustment,
                approvedBy,
                now,
                context,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new StockAdjustmentApprovalResult(adjustment.Id, journalEntryId);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<StockAdjustmentApprovalResult> CreateAndApproveAsync(
        StockAdjustmentCreateInput input,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken)
    {
        var locationType = input.LocationType.ToLowerInvariant();
        await StockAdjustmentAccessGuard.EnsureAuthorizedLocationAsync(
            dbContext,
            input.LocationId,
            locationType,
            context,
            "approve",
            cancellationToken);
        await EnsureVariantsExistAsync(input.Items, cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
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
                ValidateProjectedQuantity(
                    currentQuantity,
                    item.QuantityChange,
                    item.VariantId,
                    context.AllowNegativeStock);
                lines.Add(new StockAdjustmentLine
                {
                    VariantId = item.VariantId,
                    PreviousQuantity = currentQuantity,
                    QuantityChange = item.QuantityChange,
                    NewQuantity = projectedQuantity,
                    Reason = item.Reason!,
                    Notes = item.Notes,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            var maxId = await dbContext.StockAdjustments
                .AsNoTracking()
                .Select(adjustment => (long?)adjustment.Id)
                .MaxAsync(cancellationToken) ?? 0;
            var adjustment = new StockAdjustment
            {
                AdjustmentNumber = $"ADJ-{now:yyyy}-{(maxId + 1):D5}",
                Status = StockAdjustment.StatusPendingApproval,
                LocationId = input.LocationId,
                LocationType = locationType,
                AdjustedBy = input.AdjustedBy,
                AdjustmentDate = now,
                CreatedAt = now,
                UpdatedAt = now,
                SubmittedAt = now,
                Lines = lines
            };

            dbContext.StockAdjustments.Add(adjustment);
            await dbContext.SaveChangesAsync(cancellationToken);
            var journalEntryId = await ApplyApprovalAsync(
                adjustment,
                input.AdjustedBy,
                now,
                context,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new StockAdjustmentApprovalResult(adjustment.Id, journalEntryId);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<long?> ApplyApprovalAsync(
        StockAdjustment adjustment,
        long approvedBy,
        DateTime now,
        StockAdjustmentCreateContext context,
        CancellationToken cancellationToken)
    {
        foreach (var line in adjustment.Lines)
        {
            var inventory = await dbContext.Inventories.FirstOrDefaultAsync(row =>
                row.VariantId == line.VariantId
                && row.LocationId == adjustment.LocationId
                && row.LocationType == adjustment.LocationType,
                cancellationToken);
            var currentQuantity = inventory?.Quantity ?? 0;
            var newQuantity = currentQuantity + line.QuantityChange;
            ValidateProjectedQuantity(
                currentQuantity,
                line.QuantityChange,
                line.VariantId,
                context.AllowNegativeStock);

            if (inventory is null)
            {
                dbContext.Inventories.Add(new Inventory
                {
                    VariantId = line.VariantId,
                    LocationId = adjustment.LocationId,
                    LocationType = adjustment.LocationType,
                    Quantity = newQuantity
                });
            }
            else
            {
                inventory.Quantity = newQuantity;
            }

            line.PreviousQuantity = currentQuantity;
            line.NewQuantity = newQuantity;
            line.UpdatedAt = now;
            line.UnitCostSnapshot ??= await ResolveWeightedAverageCostSnapshotAsync(
                line.VariantId,
                cancellationToken);
            stockLedger.WriteEntryWithValuation(
                variantId: line.VariantId,
                locationId: adjustment.LocationId,
                locationType: adjustment.LocationType,
                transactionType: StockLedgerTransactionType.Adjustment,
                qtyIn: line.QuantityChange > 0 ? line.QuantityChange : 0,
                qtyOut: line.QuantityChange < 0 ? Math.Abs(line.QuantityChange) : 0,
                balanceAfter: newQuantity,
                referenceType: StockLedgerReferenceType.StockAdjustment,
                referenceId: adjustment.Id,
                remarks: line.Reason,
                createdBy: approvedBy,
                unitCost: line.UnitCostSnapshot ?? 0m);
        }

        var journalEntryId = await PostAdjustmentAccountingAsync(
            adjustment,
            approvedBy,
            now,
            context.ReadScope.BusinessId,
            cancellationToken);
        adjustment.Status = StockAdjustment.StatusApproved;
        adjustment.ApprovedBy = approvedBy;
        adjustment.ApprovedAt = now;
        adjustment.UpdatedAt = now;

        if (adjustment.SourceStockCountId.HasValue)
        {
            var sourceStockCount = await dbContext.StockCounts
                .FirstOrDefaultAsync(
                    row => row.Id == adjustment.SourceStockCountId.Value,
                    cancellationToken);
            if (sourceStockCount?.Status == StockCount.StatusAdjustmentGenerated)
            {
                sourceStockCount.Status = StockCount.StatusCompleted;
            }
        }

        return journalEntryId;
    }

    private async Task<long?> PostAdjustmentAccountingAsync(
        StockAdjustment adjustment,
        long approvedBy,
        DateTime now,
        long businessId,
        CancellationToken cancellationToken)
    {
        foreach (var line in adjustment.Lines)
        {
            line.UnitCostSnapshot ??= await ResolveWeightedAverageCostSnapshotAsync(
                line.VariantId,
                cancellationToken);
        }

        var totalValue = Math.Round(
            adjustment.Lines.Sum(line =>
                Math.Abs(line.QuantityChange) * (line.UnitCostSnapshot ?? 0m)),
            2);
        if (totalValue <= 0m)
        {
            return null;
        }

        var outletId = adjustment.LocationType.Equals(
            "outlet",
            StringComparison.OrdinalIgnoreCase)
            ? adjustment.LocationId
            : (long?)null;
        var inventoryAccountId = await postingAccountResolver.ResolveAccountIdAsync(
            businessId,
            StockAdjustmentPostingAccountSlots.Inventory,
            outletId,
            cancellationToken);
        var hasIncrease = adjustment.Lines.Any(line => line.QuantityChange > 0);
        var offsetAccountId = await postingAccountResolver.ResolveAccountIdAsync(
            businessId,
            hasIncrease
                ? StockAdjustmentPostingAccountSlots.InventoryGain
                : StockAdjustmentPostingAccountSlots.InventoryShortage,
            outletId,
            cancellationToken);
        var description = hasIncrease ? "Inventory gain" : "Inventory shortage";
        var outletLineId = outletId;
        var warehouseLineId = adjustment.LocationType.Equals(
            "warehouse",
            StringComparison.OrdinalIgnoreCase)
            ? adjustment.LocationId
            : (long?)null;
        var journal = new ManualJournalDraftInput(
            hasIncrease ? "StockGainPosting" : "StockShortagePosting",
            now,
            now,
            now.Date,
            "StockAdjustment",
            adjustment.Id.ToString(),
            adjustment.AdjustmentNumber,
            $"{description} posting for {adjustment.AdjustmentNumber}",
            outletId,
            warehouseLineId,
            null,
            null,
            [
                new ManualJournalDraftLineInput(
                    hasIncrease ? inventoryAccountId : offsetAccountId,
                    description,
                    totalValue,
                    0m,
                    outletLineId,
                    warehouseLineId,
                    null,
                    null,
                    null,
                    null),
                new ManualJournalDraftLineInput(
                    hasIncrease ? offsetAccountId : inventoryAccountId,
                    description,
                    0m,
                    totalValue,
                    outletLineId,
                    warehouseLineId,
                    null,
                    null,
                    null,
                    null)
            ]);

        var journalId = await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(
                businessId,
                approvedBy,
                journal,
                $"stock-adjustment:{adjustment.Id}"),
            cancellationToken);
        await sender.Send(
            new PostJournalEntryCommand(businessId, approvedBy, journalId),
            cancellationToken);

        return await dbContext.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.BusinessId == businessId
                && entry.IsSystemGenerated
                && entry.Status == JournalEntryStatuses.Posted
                && entry.ReferenceType == "StockAdjustment"
                && entry.ReferenceId == adjustment.Id.ToString())
            .OrderByDescending(entry => entry.Id)
            .Select(entry => (long?)entry.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<decimal> ResolveWeightedAverageCostSnapshotAsync(
        long variantId,
        CancellationToken cancellationToken)
    {
        var completedReceiptLines = await dbContext.GrnItems
            .Where(item => item.Grn.Status == "completed"
                && item.PurchaseOrderItem.VariantId == variantId)
            .Select(item => new { item.ReceivedQty, item.UnitCost })
            .ToListAsync(cancellationToken);
        if (completedReceiptLines.Count == 0)
        {
            var variant = await dbContext.ProductVariants
                .AsNoTracking()
                .Include(row => row.Product)
                .FirstOrDefaultAsync(row => row.Id == variantId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Variant {variantId} was not found while resolving stock adjustment cost snapshot.");
            return Math.Round(variant.Product.CostPrice + variant.CostAdjustment, 4);
        }

        var totalReceivedQuantity = completedReceiptLines.Sum(row => row.ReceivedQty);
        if (totalReceivedQuantity <= 0)
        {
            return 0m;
        }

        var weightedAverageCost = completedReceiptLines.Sum(
            row => row.ReceivedQty * row.UnitCost) / totalReceivedQuantity;
        return Math.Round(weightedAverageCost, 4);
    }

    private async Task EnsureVariantsExistAsync(
        IReadOnlyList<StockAdjustmentCreateLine> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (!await dbContext.ProductVariants.AnyAsync(
                    variant => variant.Id == item.VariantId,
                    cancellationToken))
            {
                throw new KeyNotFoundException(
                    $"Product variant with ID {item.VariantId} not found");
            }
        }
    }

    private static void EnsurePendingApproval(StockAdjustment adjustment)
    {
        if (!string.Equals(
                adjustment.Status,
                StockAdjustment.StatusPendingApproval,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Only {StockAdjustment.StatusPendingApproval} adjustments can be approved. Current status: {adjustment.Status}.");
        }
    }

    private static void ValidateProjectedQuantity(
        int currentQuantity,
        int quantityChange,
        long variantId,
        bool allowNegativeStock)
    {
        var projectedQuantity = currentQuantity + quantityChange;
        if (!allowNegativeStock && projectedQuantity < 0)
        {
            throw new InvalidOperationException(
                $"Adjustment would result in negative stock for variant {variantId}. Current: {currentQuantity}, Change: {quantityChange}, Result: {projectedQuantity}");
        }
    }
}
