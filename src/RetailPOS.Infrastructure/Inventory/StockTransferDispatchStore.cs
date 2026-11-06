using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferDispatchStore(
    RetailPOSDbContext dbContext,
    IStockLedgerWriter stockLedger,
    ILogger<StockTransferDispatchStore> logger)
    : IStockTransferDispatchStore
{
    public async Task<StockTransferDispatchResult> DispatchApprovedAsync(
        long transferId,
        long businessId,
        long? userId,
        CancellationToken cancellationToken)
    {
        var transfer = await StockTransferWriteRules.TransfersInBusiness(dbContext, businessId)
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (transfer is null)
            throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");

        if (!transfer.Status.Equals(StockTransfer.StatusApproved, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Can only send approved transfers. Current status: {transfer.Status}");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await DispatchTransferAsync(transfer, businessId, userId, DateTime.UtcNow, cancellationToken);
            dbContext.StockTransfers.Update(transfer);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Stock transfer {TransferId} marked as in-transit; source stock deducted",
                transferId);
            return CreateResult(transfer, wasDispatched: true);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<StockTransferDispatchResult> SubmitAndDispatchAsync(
        long transferId,
        long businessId,
        long? userId,
        bool autoApprove,
        CancellationToken cancellationToken)
    {
        var transfer = await StockTransferWriteRules.TransfersInBusiness(dbContext, businessId)
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (transfer is null)
            throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");

        var normalizedStatus = NormalizeStatus(transfer.Status);
        if (normalizedStatus == StockTransfer.StatusInTransit)
        {
            logger.LogInformation(
                "Stock transfer {TransferId} submit-and-dispatch replay ignored because it is already in transit",
                transferId);
            return CreateResult(transfer, wasDispatched: false);
        }

        if (normalizedStatus == StockTransfer.StatusCancelled
            || normalizedStatus == StockTransfer.StatusReceived
            || normalizedStatus == StockTransfer.StatusPartiallyReceived
            || normalizedStatus == StockTransfer.StatusRejected)
        {
            throw new InvalidOperationException(
                $"Cannot submit and dispatch transfer in status '{transfer.Status}'.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var timestamp = DateTime.UtcNow;
            normalizedStatus = NormalizeStatus(transfer.Status);

            if (normalizedStatus == StockTransfer.StatusDraft)
            {
                ApplySubmittedState(transfer, userId, timestamp);
                normalizedStatus = StockTransfer.StatusSubmitted;
            }

            if (normalizedStatus == StockTransfer.StatusSubmitted)
            {
                if (!autoApprove)
                    throw new InvalidOperationException(
                        "This transfer requires approval before dispatch.");

                ApplyApprovedState(transfer, userId, timestamp);
                normalizedStatus = StockTransfer.StatusApproved;
            }

            if (normalizedStatus != StockTransfer.StatusApproved)
            {
                throw new InvalidOperationException(
                    $"Cannot submit and dispatch transfer in status '{transfer.Status}'.");
            }

            await DispatchTransferAsync(transfer, businessId, userId, timestamp, cancellationToken);
            dbContext.StockTransfers.Update(transfer);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Stock transfer {TransferId} submitted and dispatched atomically by user {UserId}",
                transferId,
                userId);

            return CreateResult(transfer, wasDispatched: true);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task DispatchTransferAsync(
        StockTransfer transfer,
        long businessId,
        long? dispatchedBy,
        DateTime timestamp,
        CancellationToken cancellationToken)
    {
        var fromLocationType = NormalizeLocationType(transfer.FromLocationType);

        foreach (var item in transfer.Items)
        {
            var sourceInventory = await dbContext.Inventories.FirstOrDefaultAsync(inventory =>
                inventory.VariantId == item.VariantId
                && inventory.LocationId == transfer.FromLocationId
                && inventory.LocationType.ToLower() == fromLocationType,
                cancellationToken);

            var transferQuantity = ResolveTransferQuantity(item);
            if (sourceInventory is null || sourceInventory.Quantity < transferQuantity)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for variant {item.VariantId} at source. "
                    + $"Available: {sourceInventory?.Quantity ?? 0}, Required: {transferQuantity}");
            }

            var cost = await StockTransferCostFlow.ResolveDispatchCostAsync(
                dbContext,
                businessId,
                item.VariantId,
                transfer.FromLocationId,
                fromLocationType,
                sourceInventory.Quantity,
                transferQuantity,
                cancellationToken);
            item.UnitCost = cost.UnitCost;

            sourceInventory.Quantity -= transferQuantity;
            var transactionType = transfer.TransferType.Equals(
                    StockTransfer.TransferTypeReturn,
                    StringComparison.OrdinalIgnoreCase)
                ? StockLedgerTransactionType.ReturnTransferOut
                : StockLedgerTransactionType.TransferOut;

            StockTransferCostFlow.Write(
                stockLedger,
                cost,
                item.VariantId,
                transfer.FromLocationId,
                fromLocationType,
                transactionType,
                qtyIn: 0,
                qtyOut: transferQuantity,
                balanceAfter: sourceInventory.Quantity,
                transfer.Id,
                $"Dispatched to {transfer.ToLocationType} {transfer.ToLocationId}",
                dispatchedBy ?? transfer.CreatedBy,
                timestamp);
        }

        transfer.Status = StockTransfer.StatusInTransit;
        transfer.DispatchedBy = dispatchedBy ?? transfer.UpdatedBy ?? transfer.CreatedBy;
        transfer.DispatchedAt = timestamp;
        transfer.UpdatedBy = transfer.DispatchedBy;
        transfer.UpdatedAt = timestamp;
    }

    private static StockTransferDispatchResult CreateResult(
        StockTransfer transfer,
        bool wasDispatched) =>
        new(
            transfer.Id,
            wasDispatched,
            transfer.FromLocationType,
            transfer.FromLocationId,
            transfer.Items.Select(item => item.VariantId).ToArray());

    private static void ApplySubmittedState(
        StockTransfer transfer,
        long? submittedBy,
        DateTime timestamp)
    {
        transfer.Status = StockTransfer.StatusSubmitted;
        transfer.SubmittedBy = submittedBy;
        transfer.SubmittedAt = timestamp;
        transfer.UpdatedBy = submittedBy;
        transfer.UpdatedAt = timestamp;
    }

    private static void ApplyApprovedState(
        StockTransfer transfer,
        long? approverId,
        DateTime timestamp)
    {
        transfer.Status = StockTransfer.StatusApproved;
        transfer.ApprovedBy = approverId;
        transfer.ApprovedAt = timestamp;
        transfer.UpdatedBy = approverId;
        transfer.UpdatedAt = timestamp;
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

    private static string NormalizeStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToLowerInvariant();

    private static int ResolveTransferQuantity(StockTransferItem item) =>
        item.TransferQuantity > 0 ? item.TransferQuantity : item.Quantity;
}
