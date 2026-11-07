using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferDraftStore(
    RetailPOSDbContext dbContext,
    ILogger<StockTransferDraftStore> logger)
    : IStockTransferDraftStore
{
    public async Task<long> UpdateDraftAsync(
        long transferId,
        StockTransferCreateInput input,
        long businessId,
        CancellationToken cancellationToken)
    {
        var transfer = await StockTransferWriteRules.TransfersInBusiness(dbContext, businessId)
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);

        if (transfer is null)
            throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");

        if (!transfer.Status.Equals(StockTransfer.StatusDraft, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot edit transfer in status '{transfer.Status}'. Only draft transfers are editable.");
        }

        await StockTransferWriteRules.ValidateBusinessLocationsAndSourceStockAsync(
            dbContext,
            input,
            businessId,
            cancellationToken);
        var transferType = StockTransferWriteRules.NormalizeTransferType(input.TransferType);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            transfer.FromLocationId = input.FromLocationId;
            transfer.FromLocationType = input.FromLocationType;
            transfer.ToLocationId = input.ToLocationId;
            transfer.ToLocationType = input.ToLocationType;
            transfer.TransferDate = DateTime.SpecifyKind(input.TransferDate, DateTimeKind.Utc);
            transfer.TransferType = transferType;
            transfer.RelatedRequisitionId = input.RelatedRequisitionId;
            transfer.Notes = input.Notes;
            transfer.UpdatedAt = DateTime.UtcNow;

            dbContext.StockTransferItems.RemoveRange(transfer.Items);
            transfer.Items = input.Items.Select(item => new StockTransferItem
            {
                TransferId = transferId,
                VariantId = item.VariantId,
                Quantity = StockTransferWriteRules.ResolveRequestedQuantity(item),
                RequestedQuantity = StockTransferWriteRules.ResolveRequestedQuantity(item),
                TransferQuantity = StockTransferWriteRules.ResolveTransferQuantity(item),
                AcceptedQuantity = 0,
                RejectedQuantity = 0,
                // Set from the source weighted average at dispatch. A client value is never trusted.
                UnitCost = 0m,
                Remarks = item.Remarks
            }.WithUom(item)).ToList();

            dbContext.StockTransfers.Update(transfer);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Stock transfer {TransferId} updated", transferId);
            return transferId;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
