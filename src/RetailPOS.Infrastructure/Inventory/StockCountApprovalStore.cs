using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public sealed class StockCountApprovalStore(RetailPOSDbContext dbContext)
    : IStockCountApprovalStore
{
    public async Task ApproveAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational())
        {
            transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        }

        try
        {
            var stockCount = await dbContext.StockCounts
                .Include(candidate => candidate.Lines)
                .FirstOrDefaultAsync(candidate => candidate.Id == stockCountId, cancellationToken)
                ?? throw new KeyNotFoundException($"Stock count with ID {stockCountId} not found");

            EnsureScope(stockCount, businessId, locationId, locationType);
            if (stockCount.Status != StockCount.StatusSubmitted)
                throw new InvalidOperationException("Only Submitted stock counts can be approved.");

            if (stockCount.Lines.Any(line => !line.PhysicalStock.HasValue))
                throw new InvalidOperationException("All lines must have physical counts before approval.");

            stockCount.Status = StockCount.StatusApproved;
            stockCount.ApprovedBy = actorUserId;
            stockCount.ApprovedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private static void EnsureScope(
        StockCount stockCount,
        long businessId,
        long locationId,
        string locationType)
    {
        if (stockCount.BusinessId != businessId
            || stockCount.LocationId != locationId
            || stockCount.LocationType != locationType)
        {
            throw new UnauthorizedAccessException("You are not authorized to access this stock count.");
        }
    }
}
