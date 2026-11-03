using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockAdjustmentReversalStore(
    RetailPOSDbContext dbContext,
    IStockLedgerWriter stockLedger,
    ISender sender,
    ILogger<StockAdjustmentReversalStore> logger)
    : IStockAdjustmentReversalStore
{
    public async Task<StockAdjustmentReversalResult> ReverseAsync(
        long adjustmentId,
        long reversedBy,
        string reason,
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
            "reverse",
            cancellationToken);
        if (adjustment.Status != StockAdjustment.StatusApproved)
        {
            throw new InvalidOperationException(
                $"Only {StockAdjustment.StatusApproved} adjustments can be reversed. Current status: {adjustment.Status}.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var businessId = context.ReadScope.BusinessId;
            var now = DateTime.UtcNow;
            var originalJournal = await dbContext.JournalEntries
                .FirstOrDefaultAsync(journal =>
                    journal.BusinessId == businessId
                    && journal.ReferenceType == "StockAdjustment"
                    && journal.ReferenceId == adjustment.Id.ToString()
                    && journal.IsSystemGenerated
                    && (journal.JournalType == "StockGainPosting"
                        || journal.JournalType == "StockShortagePosting")
                    && journal.Status == JournalEntryStatuses.Posted,
                    cancellationToken);

            long? reversalJournalEntryId = null;
            if (originalJournal is not null)
            {
                reversalJournalEntryId = await sender.Send(
                    new ReverseJournalEntryCommand(
                        businessId,
                        reversedBy,
                        originalJournal.Id,
                        new ManualJournalReversalInput(
                            reason.Trim(),
                            now,
                            now.Date)),
                    cancellationToken);
            }

            foreach (var line in adjustment.Lines)
            {
                var inventory = await dbContext.Inventories.FirstOrDefaultAsync(row =>
                        row.VariantId == line.VariantId
                        && row.LocationId == adjustment.LocationId
                        && row.LocationType == adjustment.LocationType,
                    cancellationToken);
                var currentQuantity = inventory?.Quantity ?? 0;
                var restoredQuantity = currentQuantity + (-line.QuantityChange);

                if (inventory is null)
                {
                    dbContext.Inventories.Add(new Inventory
                    {
                        VariantId = line.VariantId,
                        LocationId = adjustment.LocationId,
                        LocationType = adjustment.LocationType,
                        Quantity = restoredQuantity
                    });
                }
                else
                {
                    inventory.Quantity = restoredQuantity;
                }

                line.NewQuantity = restoredQuantity;
                line.UpdatedAt = now;
                stockLedger.WriteEntryWithValuation(
                    variantId: line.VariantId,
                    locationId: adjustment.LocationId,
                    locationType: adjustment.LocationType,
                    transactionType: StockLedgerTransactionType.Adjustment,
                    qtyIn: line.QuantityChange < 0 ? Math.Abs(line.QuantityChange) : 0,
                    qtyOut: line.QuantityChange > 0 ? line.QuantityChange : 0,
                    balanceAfter: restoredQuantity,
                    referenceType: StockLedgerReferenceType.StockAdjustment,
                    referenceId: adjustment.Id,
                    remarks: $"Reversal: {reason.Trim()}",
                    createdBy: reversedBy,
                    unitCost: line.UnitCostSnapshot ?? 0m);
            }

            adjustment.Status = StockAdjustment.StatusReversed;
            adjustment.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Stock adjustment {AdjustmentNumber} reversed by user {UserId}",
                adjustment.AdjustmentNumber,
                reversedBy);
            return new StockAdjustmentReversalResult(
                adjustment.Id,
                originalJournal?.Id,
                reversalJournalEntryId);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
