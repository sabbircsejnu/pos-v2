using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferCancellationStore(
    RetailPOSDbContext dbContext,
    ILogger<StockTransferCancellationStore> logger)
    : IStockTransferCancellationStore
{
    public async Task<long> CancelAsync(
        long transferId,
        long businessId,
        string? reason,
        long? cancelledBy,
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
                .FirstOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
            if (transfer is null)
                throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");

            if (transfer.Status.Equals(StockTransfer.StatusCancelled, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Transfer is already cancelled");

            if (transfer.Status.Equals(StockTransfer.StatusInTransit, StringComparison.OrdinalIgnoreCase)
                || transfer.Status.Equals(StockTransfer.StatusReceived, StringComparison.OrdinalIgnoreCase)
                || transfer.Status.Equals(StockTransfer.StatusPartiallyReceived, StringComparison.OrdinalIgnoreCase)
                || transfer.Status.Equals(StockTransfer.StatusRejected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Cannot cancel transfer in status '{transfer.Status}'. Cancellation is allowed only before dispatch.");
            }

            transfer.Status = StockTransfer.StatusCancelled;
            transfer.CancelledBy = cancelledBy;
            transfer.CancelledAt = DateTime.UtcNow;
            transfer.UpdatedBy = cancelledBy;
            transfer.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Stock transfer {TransferId} cancelled by user {UserId}. Reason: {Reason}",
                transferId,
                cancelledBy,
                reason);
            return transfer.Id;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
