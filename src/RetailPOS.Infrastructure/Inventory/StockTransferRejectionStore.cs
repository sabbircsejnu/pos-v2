using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferRejectionStore(
    RetailPOSDbContext dbContext,
    IStockLedgerWriter stockLedger,
    IStockTransferReceiptAuditWriter auditWriter,
    ILogger<StockTransferRejectionStore> logger)
    : IStockTransferRejectionStore
{
    public async Task<long> RejectAsync(
        long transferId,
        long businessId,
        string? reason,
        long? rejectedBy,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (dbContext.Database.IsNpgsql())
            {
                await dbContext.StockTransfers
                    .FromSqlInterpolated(
                        $"SELECT * FROM stock_transfers WHERE id = {transferId} FOR UPDATE")
                    .Select(candidate => candidate.Id)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            var transfer = await StockTransferWriteRules.TransfersInBusiness(dbContext, businessId)
                .Include(candidate => candidate.Items)
                .FirstOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
            if (transfer is null)
                throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");

            if (!transfer.Status.Equals(StockTransfer.StatusInTransit, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "This transfer must be In Transit before it can be rejected.");
            }

            if (transfer.OriginalTransferId.HasValue)
            {
                throw new InvalidOperationException(
                    "A return of rejected stock cannot be rejected again. Record any units not received as missing or damaged.");
            }

            transfer.Status = StockTransfer.StatusRejected;
            transfer.RejectedBy = rejectedBy;
            transfer.RejectedAt = DateTime.UtcNow;
            transfer.UpdatedBy = rejectedBy;
            transfer.UpdatedAt = DateTime.UtcNow;

            foreach (var item in transfer.Items)
            {
                var rejectedQuantity = ResolveTransferQuantity(item);
                item.AcceptedQuantity = 0;
                item.RejectedQuantity = rejectedQuantity;
                var locationType = NormalizeLocationType(transfer.ToLocationType);
                var inventory = await dbContext.Inventories.FirstOrDefaultAsync(
                    row => row.VariantId == item.VariantId
                        && row.LocationId == transfer.ToLocationId
                        && row.LocationType.ToLower() == locationType,
                    cancellationToken);
                StockTransferWriteRules.ClaimForEvidenceOnlyMovement(dbContext, inventory);

                stockLedger.WriteEntryWithValuation(
                    variantId: item.VariantId,
                    locationId: transfer.ToLocationId,
                    locationType: locationType,
                    transactionType: StockLedgerTransactionType.TransferRejection,
                    qtyIn: 0,
                    qtyOut: 0,
                    balanceAfter: inventory?.Quantity ?? 0,
                    referenceType: StockLedgerReferenceType.StockTransfer,
                    referenceId: transfer.Id,
                    remarks: $"Transfer rejected. Reason: {reason}",
                    createdBy: rejectedBy ?? transfer.CreatedBy,
                    unitCost: item.UnitCost);
            }

            await StockTransferWriteRules.CreateSystemReturnAsync(
                dbContext,
                transfer,
                rejectedBy ?? transfer.CreatedBy,
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordAsync(
                new StockTransferReceiptAudit(
                    transfer.Id,
                    transfer.TransferNo,
                    transfer.Status,
                    transfer.ToLocationId,
                    transfer.ToLocationType,
                    rejectedBy,
                    reason,
                    transfer.Items
                        .Select(item => new StockTransferReceiptAuditLine(
                            item.VariantId,
                            item.RejectedQuantity,
                            0,
                            item.RejectedQuantity,
                            0,
                            0,
                            item.Remarks))
                        .ToArray()),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Stock transfer {TransferId} rejected by user {UserId}. Reason: {Reason}",
                transferId,
                rejectedBy,
                reason);
            return transferId;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static int ResolveTransferQuantity(StockTransferItem item) =>
        item.TransferQuantity > 0 ? item.TransferQuantity : item.Quantity;

    private static string NormalizeLocationType(string locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
            throw new InvalidOperationException("Location type is required.");

        var normalized = locationType.Trim().ToLowerInvariant();
        if (normalized != "outlet" && normalized != "warehouse")
            throw new InvalidOperationException($"Invalid location type '{locationType}'.");

        return normalized;
    }
}
