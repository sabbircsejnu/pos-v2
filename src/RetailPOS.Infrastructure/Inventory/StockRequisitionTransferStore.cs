using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.StockTransfers;

namespace RetailPOS.Infrastructure.StockRequisitions;

public sealed class StockRequisitionTransferStore(
    RetailPOSDbContext dbContext,
    ILogger<StockRequisitionTransferStore> logger)
    : IStockRequisitionTransferStore
{
    public async Task<long> ConvertToTransferAsync(
        long requisitionId,
        long businessId,
        long createdBy,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (dbContext.Database.IsNpgsql())
            {
                await dbContext.StockRequisitions
                    .FromSqlInterpolated(
                        $"SELECT * FROM stock_requisitions WHERE id = {requisitionId} FOR UPDATE")
                    .Select(candidate => candidate.Id)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            var requisition = await dbContext.StockRequisitions
                .Include(candidate => candidate.Lines)
                .FirstOrDefaultAsync(candidate => candidate.Id == requisitionId, cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Stock requisition with ID {requisitionId} not found");

            if (!await StockTransferWriteRules.LocationBelongsToBusinessAsync(
                    dbContext,
                    requisition.SourceLocationId,
                    requisition.SourceLocationType,
                    businessId,
                    cancellationToken)
                || !await StockTransferWriteRules.LocationBelongsToBusinessAsync(
                    dbContext,
                    requisition.RequestingLocationId,
                    requisition.RequestingLocationType,
                    businessId,
                    cancellationToken))
            {
                throw new UnauthorizedAccessException(
                    "Stock transfer locations must belong to the current business.");
            }

            if (!requisition.Status.Equals(StockRequisition.StatusApproved, StringComparison.OrdinalIgnoreCase)
                && !requisition.Status.Equals(StockRequisition.StatusPartiallyFulfilled, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Requisition in status '{requisition.Status}' cannot be converted to transfer.");
            }

            var pendingLines = requisition.Lines
                .Select(line => new
                {
                    Line = line,
                    Pending = Math.Max(0, line.RequestedQuantity - line.FulfilledQuantity)
                })
                .Where(candidate => candidate.Pending > 0)
                .ToList();

            if (pendingLines.Count == 0)
                throw new InvalidOperationException("Requisition is already fully fulfilled.");

            var now = DateTime.UtcNow;
            var transfer = new StockTransfer
            {
                TransferNo = await GenerateTransferNumberAsync(cancellationToken),
                TransferType = StockTransfer.TransferTypeRequisition,
                RelatedRequisitionId = requisition.Id,
                FromLocationId = requisition.SourceLocationId,
                FromLocationType = requisition.SourceLocationType,
                ToLocationId = requisition.RequestingLocationId,
                ToLocationType = requisition.RequestingLocationType,
                TransferDate = now,
                Status = StockTransfer.StatusDraft,
                CreatedBy = createdBy,
                CreatedAt = now,
                Notes = $"Created from requisition {requisition.RequisitionNo}",
                Items = pendingLines.Select(candidate => new StockTransferItem
                {
                    VariantId = candidate.Line.VariantId,
                    Quantity = candidate.Pending,
                    RequestedQuantity = candidate.Line.RequestedQuantity,
                    TransferQuantity = candidate.Pending,
                    AcceptedQuantity = 0,
                    RejectedQuantity = 0,
                    UnitCost = 0m,
                    Remarks = candidate.Line.Remarks
                }).ToList()
            };

            foreach (var pending in pendingLines)
                pending.Line.FulfilledQuantity += pending.Pending;

            requisition.Status = requisition.Lines.All(
                line => line.FulfilledQuantity >= line.RequestedQuantity)
                ? StockRequisition.StatusFullyFulfilled
                : StockRequisition.StatusPartiallyFulfilled;
            requisition.UpdatedBy = createdBy;
            requisition.UpdatedAt = now;

            dbContext.StockTransfers.Add(transfer);
            dbContext.StockRequisitions.Update(requisition);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Stock requisition {RequisitionNo} ({RequisitionId}) converted to transfer {TransferNo} ({TransferId}) by user {UserId}",
                requisition.RequisitionNo,
                requisition.Id,
                transfer.TransferNo,
                transfer.Id,
                createdBy);
            return transfer.Id;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<string> GenerateTransferNumberAsync(CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year;
        var maxId = await dbContext.StockTransfers.MaxAsync(
            candidate => (long?)candidate.Id,
            cancellationToken) ?? 0;
        return $"TRF-{year}-{(maxId + 1):D5}";
    }
}
