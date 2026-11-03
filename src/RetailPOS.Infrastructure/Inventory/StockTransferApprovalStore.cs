using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferApprovalStore(
    RetailPOSDbContext dbContext,
    ILogger<StockTransferApprovalStore> logger)
    : IStockTransferApprovalStore
{
    public async Task<long> ApproveAsync(
        long transferId,
        long businessId,
        long? approvedBy,
        CancellationToken cancellationToken)
    {
        var transfer = await StockTransferWriteRules.TransfersInBusiness(dbContext, businessId)
            .FirstOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (transfer is null)
            throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");

        if (!transfer.Status.Equals(StockTransfer.StatusSubmitted, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Can only approve submitted transfers. Current status: {transfer.Status}");
        }

        var timestamp = DateTime.UtcNow;
        transfer.Status = StockTransfer.StatusApproved;
        transfer.ApprovedBy = approvedBy;
        transfer.ApprovedAt = timestamp;
        transfer.UpdatedBy = approvedBy;
        transfer.UpdatedAt = timestamp;

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Stock transfer {TransferId} approved by user {UserId}",
            transferId,
            approvedBy);
        return transferId;
    }
}
