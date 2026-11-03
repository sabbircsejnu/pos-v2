using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferRejectionStore(
    RetailPOSDbContext dbContext,
    IStockLedgerWriter stockLedger,
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
                var inventoryBalance = await dbContext.Inventories
                    .Where(inventory =>
                        inventory.VariantId == item.VariantId
                        && inventory.LocationId == transfer.ToLocationId
                        && inventory.LocationType.ToLower() == locationType)
                    .Select(inventory => (int?)inventory.Quantity)
                    .FirstOrDefaultAsync(cancellationToken) ?? 0;

                stockLedger.WriteEntry(
                    variantId: item.VariantId,
                    locationId: transfer.ToLocationId,
                    locationType: transfer.ToLocationType,
                    transactionType: StockLedgerTransactionType.TransferRejection,
                    qtyIn: 0,
                    qtyOut: 0,
                    balanceAfter: inventoryBalance,
                    referenceType: StockLedgerReferenceType.StockTransfer,
                    referenceId: transfer.Id,
                    remarks: $"Transfer rejected. Reason: {reason}",
                    createdBy: rejectedBy ?? transfer.CreatedBy);
            }

            var returnItems = transfer.Items
                .Select(item => new
                {
                    item.VariantId,
                    Quantity = item.RejectedQuantity > 0
                        ? item.RejectedQuantity
                        : ResolveTransferQuantity(item)
                })
                .Where(item => item.Quantity > 0)
                .ToArray();

            if (returnItems.Length > 0)
            {
                var now = DateTime.UtcNow;
                var maxId = await dbContext.StockTransfers
                    .MaxAsync(candidate => (long?)candidate.Id, cancellationToken) ?? 0;
                var returnTransfer = new StockTransfer
                {
                    TransferNo = $"TRF-{now.Year}-{(maxId + 1):D5}",
                    TransferType = StockTransfer.TransferTypeReturn,
                    FromLocationId = transfer.ToLocationId,
                    FromLocationType = transfer.ToLocationType,
                    ToLocationId = transfer.FromLocationId,
                    ToLocationType = transfer.FromLocationType,
                    TransferDate = now,
                    Status = StockTransfer.StatusDraft,
                    CreatedBy = rejectedBy ?? transfer.CreatedBy,
                    CreatedAt = now,
                    Notes = $"Auto-created return transfer for rejected transfer #{transfer.TransferNo ?? transfer.Id.ToString()}.",
                    Items = returnItems.Select(item => new StockTransferItem
                    {
                        VariantId = item.VariantId,
                        Quantity = item.Quantity,
                        RequestedQuantity = item.Quantity,
                        TransferQuantity = item.Quantity,
                        AcceptedQuantity = 0,
                        RejectedQuantity = 0,
                        UnitCost = 0m
                    }).ToList()
                };
                dbContext.StockTransfers.Add(returnTransfer);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
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
