using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferCreationStore(
    RetailPOSDbContext dbContext,
    ILogger<StockTransferCreationStore> logger)
    : IStockTransferCreationStore
{
    public async Task<long> CreateAsync(
        StockTransferCreateInput input,
        long businessId,
        long? createdBy,
        CancellationToken cancellationToken)
    {
        await StockTransferWriteRules.ValidateBusinessLocationsAndSourceStockAsync(
            dbContext,
            input,
            businessId,
            cancellationToken);

        var year = DateTime.UtcNow.Year;
        var maxId = await dbContext.StockTransfers.MaxAsync(
            transfer => (long?)transfer.Id,
            cancellationToken) ?? 0;
        var transferNo = $"TRF-{year}-{(maxId + 1):D5}";

        var transfer = new StockTransfer
        {
            TransferNo = transferNo,
            TransferType = StockTransferWriteRules.NormalizeTransferType(input.TransferType),
            RelatedRequisitionId = input.RelatedRequisitionId,
            FromLocationId = input.FromLocationId,
            FromLocationType = input.FromLocationType,
            ToLocationId = input.ToLocationId,
            ToLocationType = input.ToLocationType,
            TransferDate = DateTime.SpecifyKind(input.TransferDate, DateTimeKind.Utc),
            Status = StockTransfer.StatusDraft,
            Notes = input.Notes,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            Items = input.Items.Select(item => new StockTransferItem
            {
                VariantId = item.VariantId,
                Quantity = StockTransferWriteRules.ResolveRequestedQuantity(item),
                RequestedQuantity = StockTransferWriteRules.ResolveRequestedQuantity(item),
                TransferQuantity = StockTransferWriteRules.ResolveTransferQuantity(item),
                AcceptedQuantity = 0,
                RejectedQuantity = 0,
                // Set from the source weighted average at dispatch. A client value is never trusted.
                UnitCost = 0m,
                Remarks = item.Remarks
            }).ToList()
        };

        await dbContext.StockTransfers.AddAsync(transfer, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Stock transfer {TransferNo} ({TransferId}) created by user {UserId}",
            transfer.TransferNo,
            transfer.Id,
            createdBy);
        return transfer.Id;
    }

}
