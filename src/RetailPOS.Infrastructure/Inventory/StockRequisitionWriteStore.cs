using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockRequisitions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockRequisitions;

public sealed class StockRequisitionWriteStore(
    RetailPOSDbContext dbContext,
    ILogger<StockRequisitionWriteStore> logger)
    : IStockRequisitionWriteStore
{
    public async Task<long> CreateAsync(
        StockRequisitionWriteInput input,
        long businessId,
        long requestedBy,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await ValidateBusinessLocationsAsync(input, businessId, cancellationToken);
            var year = DateTime.UtcNow.Year;
            var maxId = await dbContext.StockRequisitions.MaxAsync(
                candidate => (long?)candidate.Id,
                cancellationToken) ?? 0;
            var now = DateTime.UtcNow;
            var requisition = new StockRequisition
            {
                RequisitionNo = $"REQ-{year}-{(maxId + 1):D5}",
                RequestingLocationId = input.RequestingLocationId,
                RequestingLocationType = input.RequestingLocationType,
                SourceLocationId = input.SourceLocationId,
                SourceLocationType = input.SourceLocationType,
                RequestedBy = requestedBy,
                RequestDate = now,
                Status = StockRequisition.StatusDraft,
                Notes = input.Notes,
                CreatedAt = now,
                Lines = input.Lines.Select(line => new StockRequisitionLine
                {
                    VariantId = line.VariantId,
                    RequestedQuantity = line.RequestedQuantity,
                    FulfilledQuantity = 0,
                    Remarks = line.Remarks
                }).ToList()
            };

            dbContext.StockRequisitions.Add(requisition);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Stock requisition {RequisitionNo} ({RequisitionId}) created by user {UserId}",
                requisition.RequisitionNo,
                requisition.Id,
                requestedBy);
            return requisition.Id;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task UpdateAsync(
        long id,
        StockRequisitionWriteInput input,
        long businessId,
        long updatedBy,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await ValidateBusinessLocationsAsync(input, businessId, cancellationToken);
            if (dbContext.Database.IsNpgsql())
            {
                await dbContext.StockRequisitions
                    .FromSqlInterpolated(
                        $"SELECT * FROM stock_requisitions WHERE id = {id} FOR UPDATE")
                    .Select(candidate => candidate.Id)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            var requisition = await dbContext.StockRequisitions
                .Include(candidate => candidate.Lines)
                .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException(
                    $"Stock requisition with ID {id} not found");

            if (!await LocationBelongsToBusinessAsync(
                    requisition.RequestingLocationId,
                    requisition.RequestingLocationType,
                    businessId,
                    cancellationToken)
                || !await LocationBelongsToBusinessAsync(
                    requisition.SourceLocationId,
                    requisition.SourceLocationType,
                    businessId,
                    cancellationToken))
            {
                throw new UnauthorizedAccessException(
                    "Stock requisition locations must belong to the current business.");
            }

            if (!requisition.Status.Equals(
                    StockRequisition.StatusDraft,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Cannot edit requisition in status '{requisition.Status}'. Only draft requisitions are editable.");
            }

            requisition.RequestingLocationId = input.RequestingLocationId;
            requisition.RequestingLocationType = input.RequestingLocationType;
            requisition.SourceLocationId = input.SourceLocationId;
            requisition.SourceLocationType = input.SourceLocationType;
            requisition.Notes = input.Notes;
            requisition.UpdatedBy = updatedBy;
            requisition.UpdatedAt = DateTime.UtcNow;

            dbContext.StockRequisitionLines.RemoveRange(requisition.Lines);
            requisition.Lines = input.Lines.Select(line => new StockRequisitionLine
            {
                RequisitionId = requisition.Id,
                VariantId = line.VariantId,
                RequestedQuantity = line.RequestedQuantity,
                FulfilledQuantity = 0,
                Remarks = line.Remarks
            }).ToList();

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Stock requisition {RequisitionNo} ({RequisitionId}) updated by user {UserId}",
                requisition.RequisitionNo,
                requisition.Id,
                updatedBy);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task ValidateBusinessLocationsAsync(
        StockRequisitionWriteInput input,
        long businessId,
        CancellationToken cancellationToken)
    {
        if (!await LocationBelongsToBusinessAsync(
                input.RequestingLocationId,
                input.RequestingLocationType,
                businessId,
                cancellationToken)
            || !await LocationBelongsToBusinessAsync(
                input.SourceLocationId,
                input.SourceLocationType,
                businessId,
                cancellationToken))
        {
            throw new UnauthorizedAccessException(
                "Stock requisition locations must belong to the current business.");
        }
    }

    private Task<bool> LocationBelongsToBusinessAsync(
        long locationId,
        string locationType,
        long businessId,
        CancellationToken cancellationToken) =>
        locationType.Equals("outlet", StringComparison.OrdinalIgnoreCase)
            ? dbContext.Outlets.AnyAsync(
                location => location.Id == locationId && location.BusinessId == businessId,
                cancellationToken)
            : locationType.Equals("warehouse", StringComparison.OrdinalIgnoreCase)
                ? dbContext.Warehouses.AnyAsync(
                    location => location.Id == locationId && location.BusinessId == businessId,
                    cancellationToken)
                : Task.FromResult(false);
}
