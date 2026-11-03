using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public sealed class StockCountCreationStore(RetailPOSDbContext dbContext)
    : IStockCountCreationStore
{
    private const string ActiveCountIndex = "IX_stock_counts_business_id_location_type_location_id";

    public async Task<long> CreateAsync(
        StockCountCreateInput input,
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
            var hasActive = await dbContext.StockCounts.AnyAsync(stockCount =>
                    stockCount.BusinessId == input.BusinessId
                    && stockCount.LocationId == input.LocationId
                    && stockCount.LocationType == input.LocationType
                    && (stockCount.Status == StockCount.StatusDraft
                        || stockCount.Status == StockCount.StatusSubmitted),
                cancellationToken);
            if (hasActive)
            {
                throw new InvalidOperationException(
                    "An active stock count already exists for this location.");
            }

            var baselineReadStartedAt = DateTime.UtcNow;
            var variants = await dbContext.ProductVariants
                .AsNoTracking()
                .Include(variant => variant.Product)
                .Where(variant => variant.Product.Status == ProductStatus.Active)
                .Select(variant => new
                {
                    variant.Id,
                    variant.Name,
                    variant.ProductId,
                    ProductName = variant.Product.Name,
                    ProductCode = variant.Product.ProductCode
                        ?? variant.Product.Sku
                        ?? variant.Sku
                })
                .ToListAsync(cancellationToken);

            var stocks = await dbContext.Inventories
                .AsNoTracking()
                .Where(inventory =>
                    inventory.LocationId == input.LocationId
                    && inventory.LocationType == input.LocationType)
                .GroupBy(inventory => inventory.VariantId)
                .Select(group => new
                {
                    VariantId = group.Key,
                    Quantity = group.Sum(inventory => inventory.Quantity)
                })
                .ToDictionaryAsync(item => item.VariantId, item => item.Quantity, cancellationToken);
            var baselineReadCompletedAt = DateTime.UtcNow;

            var now = DateTime.UtcNow;
            var prefix = $"SC-{now:yyyyMMdd}";
            var todayCount = await dbContext.StockCounts
                .AsNoTracking()
                .CountAsync(
                    stockCount => stockCount.StockCountNo.StartsWith(prefix),
                    cancellationToken);
            var stockCount = new StockCount
            {
                StockCountNo = $"{prefix}-{todayCount + 1:0000}",
                BusinessId = input.BusinessId,
                LocationId = input.LocationId,
                LocationType = input.LocationType,
                StockCountDate = input.StockCountDate == default
                    ? now.Date
                    : input.StockCountDate.Date,
                Status = StockCount.StatusDraft,
                CountRevision = 0,
                BaselineInventoryReadStartedAtUtc = baselineReadStartedAt,
                BaselineInventoryReadCompletedAtUtc = baselineReadCompletedAt,
                BaselineConsistencyStatus = "InventoryStatementSnapshot",
                MovementReconciliationStatus = StockCount.MovementReconciliationNotStarted,
                Remarks = input.Remarks,
                TotalItems = variants.Count,
                CreatedBy = input.CreatedBy,
                CreatedAt = now,
                Lines = variants.Select(variant => new StockCountLine
                {
                    ProductId = variant.ProductId,
                    VariantId = variant.Id,
                    ProductName = variant.ProductName,
                    ProductCode = variant.ProductCode ?? string.Empty,
                    VariantName = variant.Name,
                    CurrentStock = stocks.TryGetValue(variant.Id, out var quantity)
                        ? quantity
                        : 0m,
                    PhysicalStock = null,
                    Difference = null,
                    Remarks = null
                }).ToList()
            };

            dbContext.StockCounts.Add(stockCount);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return stockCount.Id;
        }
        catch (DbUpdateException exception) when (IsActiveCountConflict(exception))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw new InvalidOperationException(
                "An active stock count already exists for this location.",
                exception);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static bool IsActiveCountConflict(DbUpdateException exception) =>
        exception.GetBaseException() is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ActiveCountIndex
        };
}
