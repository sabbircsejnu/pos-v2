using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferReceiveStore(
    RetailPOSDbContext dbContext,
    IStockLedgerWriter stockLedger,
    ILogger<StockTransferReceiveStore> logger)
    : IStockTransferReceiveStore
{
    public async Task<StockTransferReceiveResult> ReceiveAsync(
        long transferId,
        long businessId,
        StockTransferReceiveInput? input,
        long? receivedBy,
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
                    "This transfer must be In Transit before it can be received.");
            }

            var normalizedToType = NormalizeLocationType(transfer.ToLocationType);
            var requestedLines = input?.Items?.ToDictionary(item => item.VariantId);
            var totalAccepted = 0;
            var totalRejected = 0;

            foreach (var item in transfer.Items)
            {
                var destinationInventory = await dbContext.Inventories.FirstOrDefaultAsync(
                    inventory => inventory.VariantId == item.VariantId
                        && inventory.LocationId == transfer.ToLocationId
                        && inventory.LocationType.ToLower() == normalizedToType,
                    cancellationToken);

                if (destinationInventory is null)
                {
                    destinationInventory = new Inventory
                    {
                        VariantId = item.VariantId,
                        LocationId = transfer.ToLocationId,
                        LocationType = normalizedToType,
                        Quantity = 0
                    };
                    dbContext.Inventories.Add(destinationInventory);
                }

                var transferQuantity = ResolveTransferQuantity(item);
                var acceptedQuantity = transferQuantity;
                var rejectedQuantity = 0;
                var remarks = item.Remarks;

                if (requestedLines is not null
                    && requestedLines.TryGetValue(item.VariantId, out var receiveLine))
                {
                    acceptedQuantity = receiveLine.AcceptedQuantity;
                    rejectedQuantity = receiveLine.RejectedQuantity;
                    remarks = receiveLine.Remarks;

                    if (acceptedQuantity < 0 || rejectedQuantity < 0)
                    {
                        throw new InvalidOperationException(
                            "Accepted and rejected quantities cannot be negative.");
                    }

                    if (acceptedQuantity + rejectedQuantity > transferQuantity)
                    {
                        throw new InvalidOperationException(
                            $"Accepted + rejected quantities exceed transfer quantity for variant {item.VariantId}. "
                            + $"Transfer: {transferQuantity}, Accepted: {acceptedQuantity}, Rejected: {rejectedQuantity}");
                    }
                }

                item.AcceptedQuantity = acceptedQuantity;
                item.RejectedQuantity = rejectedQuantity;
                item.Remarks = remarks;
                destinationInventory.Quantity += acceptedQuantity;
                totalAccepted += acceptedQuantity;
                totalRejected += rejectedQuantity;

                var transactionType = transfer.TransferType.Equals(
                    StockTransfer.TransferTypeReturn,
                    StringComparison.OrdinalIgnoreCase)
                    ? StockLedgerTransactionType.ReturnTransferIn
                    : StockLedgerTransactionType.TransferIn;

                stockLedger.WriteEntryWithValuation(
                    variantId: item.VariantId,
                    locationId: transfer.ToLocationId,
                    locationType: normalizedToType,
                    transactionType: transactionType,
                    qtyIn: acceptedQuantity,
                    qtyOut: 0,
                    balanceAfter: destinationInventory.Quantity,
                    referenceType: StockLedgerReferenceType.StockTransfer,
                    referenceId: transfer.Id,
                    remarks: $"Received from {transfer.FromLocationType} {transfer.FromLocationId}",
                    createdBy: receivedBy ?? transfer.CreatedBy,
                    unitCost: item.UnitCost);
            }

            transfer.Status = ResolveReceiveStatus(totalAccepted, totalRejected);
            transfer.ReceivedBy = receivedBy ?? transfer.UpdatedBy ?? transfer.CreatedBy;
            transfer.ReceivedAt = DateTime.UtcNow;
            transfer.Notes = string.IsNullOrWhiteSpace(input?.Notes) ? transfer.Notes : input!.Notes;
            transfer.UpdatedBy = transfer.ReceivedBy;
            transfer.UpdatedAt = DateTime.UtcNow;

            if (totalRejected > 0)
                await CreateReturnTransferForRejectedAsync(transfer, transfer.ReceivedBy, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Stock transfer {TransferId} received; destination stock updated",
                transferId);

            return new StockTransferReceiveResult(
                transfer.Id,
                transfer.ToLocationType,
                transfer.ToLocationId,
                transfer.Items.Select(item => item.VariantId).ToArray());
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task CreateReturnTransferForRejectedAsync(
        StockTransfer rejectedTransfer,
        long? createdBy,
        CancellationToken cancellationToken)
    {
        var returnItems = rejectedTransfer.Items
            .Select(item => new
            {
                item.VariantId,
                Quantity = item.RejectedQuantity > 0
                    ? item.RejectedQuantity
                    : ResolveTransferQuantity(item)
            })
            .Where(item => item.Quantity > 0)
            .ToArray();

        if (returnItems.Length == 0)
            return;

        var now = DateTime.UtcNow;
        var maxId = await dbContext.StockTransfers
            .MaxAsync(candidate => (long?)candidate.Id, cancellationToken) ?? 0;
        var returnTransfer = new StockTransfer
        {
            TransferNo = $"TRF-{now.Year}-{(maxId + 1):D5}",
            TransferType = StockTransfer.TransferTypeReturn,
            FromLocationId = rejectedTransfer.ToLocationId,
            FromLocationType = rejectedTransfer.ToLocationType,
            ToLocationId = rejectedTransfer.FromLocationId,
            ToLocationType = rejectedTransfer.FromLocationType,
            TransferDate = now,
            Status = StockTransfer.StatusDraft,
            CreatedBy = createdBy,
            CreatedAt = now,
            Notes = $"Auto-created return transfer for rejected transfer #{rejectedTransfer.TransferNo ?? rejectedTransfer.Id.ToString()}.",
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

    private static int ResolveTransferQuantity(StockTransferItem item) =>
        item.TransferQuantity > 0 ? item.TransferQuantity : item.Quantity;

    private static string ResolveReceiveStatus(int totalAccepted, int totalRejected)
    {
        if (totalAccepted > 0 && totalRejected > 0)
            return StockTransfer.StatusPartiallyReceived;

        if (totalAccepted == 0 && totalRejected > 0)
            return StockTransfer.StatusRejected;

        return StockTransfer.StatusReceived;
    }

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
