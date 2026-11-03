using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockRequisitions;

public sealed class StockRequisitionLifecycleStore(
    RetailPOSDbContext dbContext,
    ILogger<StockRequisitionLifecycleStore> logger)
    : IStockRequisitionLifecycleStore
{
    public Task SubmitAsync(
        long id,
        long businessId,
        long submittedBy,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            businessId,
            StockRequisition.StatusDraft,
            currentStatus => $"Can only submit draft requisitions. Current status: {currentStatus}",
            requisition =>
            {
                requisition.Status = StockRequisition.StatusSubmitted;
                requisition.SubmittedBy = submittedBy;
                requisition.SubmittedAt = DateTime.UtcNow;
                requisition.UpdatedBy = submittedBy;
                requisition.UpdatedAt = DateTime.UtcNow;
            },
            "submitted",
            submittedBy,
            cancellationToken);

    public Task ApproveAsync(
        long id,
        long businessId,
        long approvedBy,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            businessId,
            StockRequisition.StatusSubmitted,
            currentStatus => $"Can only approve submitted requisitions. Current status: {currentStatus}",
            requisition =>
            {
                requisition.Status = StockRequisition.StatusApproved;
                requisition.ApprovedBy = approvedBy;
                requisition.ApprovedAt = DateTime.UtcNow;
                requisition.UpdatedBy = approvedBy;
                requisition.UpdatedAt = DateTime.UtcNow;
            },
            "approved",
            approvedBy,
            cancellationToken);

    public Task RejectAsync(
        long id,
        long businessId,
        long rejectedBy,
        string? reason,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            businessId,
            StockRequisition.StatusSubmitted,
            currentStatus => $"Can only reject submitted requisitions. Current status: {currentStatus}",
            requisition =>
            {
                requisition.Status = StockRequisition.StatusRejected;
                requisition.RejectedBy = rejectedBy;
                requisition.RejectedAt = DateTime.UtcNow;
                requisition.RejectionReason = reason;
                requisition.UpdatedBy = rejectedBy;
                requisition.UpdatedAt = DateTime.UtcNow;
            },
            "rejected",
            rejectedBy,
            cancellationToken);

    private async Task TransitionAsync(
        long id,
        long businessId,
        string expectedStatus,
        Func<string, string> eligibilityError,
        Action<StockRequisition> applyTransition,
        string transitionName,
        long actorId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (dbContext.Database.IsNpgsql())
            {
                await dbContext.StockRequisitions
                    .FromSqlInterpolated(
                        $"SELECT * FROM stock_requisitions WHERE id = {id} FOR UPDATE")
                    .Select(candidate => candidate.Id)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            var requisition = await dbContext.StockRequisitions
                .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException($"Stock requisition with ID {id} not found");

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

            if (!requisition.Status.Equals(expectedStatus, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(eligibilityError(requisition.Status));

            applyTransition(requisition);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Stock requisition {RequisitionNo} ({RequisitionId}) {Transition} by user {UserId}",
                requisition.RequisitionNo,
                requisition.Id,
                transitionName,
                actorId);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
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
