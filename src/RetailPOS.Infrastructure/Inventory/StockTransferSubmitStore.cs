using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferSubmitStore(
    RetailPOSDbContext dbContext,
    ILogger<StockTransferSubmitStore> logger)
    : IStockTransferSubmitStore
{
    public async Task<long> SubmitAsync(
        long transferId,
        long businessId,
        long? submittedBy,
        CancellationToken cancellationToken)
    {
        var transfer = await StockTransferWriteRules.TransfersInBusiness(dbContext, businessId)
            .FirstOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        if (transfer is null)
            throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");

        if (!transfer.Status.Equals(StockTransfer.StatusDraft, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Can only submit draft transfers. Current status: {transfer.Status}");
        }

        var timestamp = DateTime.UtcNow;
        transfer.Status = StockTransfer.StatusSubmitted;
        transfer.SubmittedBy = submittedBy;
        transfer.SubmittedAt = timestamp;
        transfer.UpdatedBy = submittedBy;
        transfer.UpdatedAt = timestamp;

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Stock transfer {TransferNo} ({TransferId}) submitted by user {UserId}",
            transfer.TransferNo,
            transferId,
            submittedBy);
        return transferId;
    }
}
