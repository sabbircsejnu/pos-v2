using System.Data;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public sealed class StockCountAdjustmentDraftStore(
    RetailPOSDbContext dbContext,
    ILogger<StockCountAdjustmentDraftStore> logger)
    : IStockCountAdjustmentDraftStore
{
    private const string SourceStockCountIndex = "IX_stock_adjustments_source_stock_count_id";
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> GenerationLocks = new();
    private readonly StockCountAdjustmentDraftBuilder draftBuilder = new(dbContext);

    public async Task GenerateAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var generationLock = GenerationLocks.GetOrAdd(stockCountId, _ => new SemaphoreSlim(1, 1));
        await generationLock.WaitAsync(cancellationToken);
        try
        {
            await GenerateUnderLockAsync(
                stockCountId,
                businessId,
                locationId,
                locationType,
                actorUserId,
                cancellationToken);
        }
        finally
        {
            generationLock.Release();
        }
    }

    private async Task GenerateUnderLockAsync(
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
            if (stockCount.Status != StockCount.StatusApproved)
                throw new InvalidOperationException(
                    "Stock adjustment draft can only be generated from Approved stock counts.");

            if (stockCount.MovementReconciliationStatus
                == StockCount.MovementReconciliationUnresolved)
            {
                throw new InvalidOperationException(
                    "Stock movement reconciliation must be resolved before generating an adjustment draft.");
            }

            if (await dbContext.StockAdjustments
                    .AsNoTracking()
                    .AnyAsync(adjustment => adjustment.SourceStockCountId == stockCount.Id, cancellationToken))
            {
                throw new InvalidOperationException(
                    "Adjustment draft has already been generated for this stock count.");
            }

            var adjustment = await draftBuilder.StageDraftAsync(stockCount, actorUserId, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Stock adjustment {AdjustmentNumber} drafted from stock count {StockCountNo}",
                adjustment.AdjustmentNumber,
                stockCount.StockCountNo);
        }
        catch (DbUpdateException exception) when (IsSourceStockCountConflict(exception))
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);

            throw new InvalidOperationException(
                "Stock adjustment draft can only be generated from Approved stock counts.",
                exception);
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

    private static bool IsSourceStockCountConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: SourceStockCountIndex
                })
            {
                return true;
            }
        }

        return false;
    }
}
