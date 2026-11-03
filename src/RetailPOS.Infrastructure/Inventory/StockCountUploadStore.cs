using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public sealed class StockCountUploadStore(
    RetailPOSDbContext dbContext,
    ILogger<StockCountUploadStore> logger)
    : IStockCountUploadStore
{
    public async Task<long> UploadAsync(
        StockCountUploadInput input,
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
            var stockCountQuery = dbContext.StockCounts;
            var stockCount = dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL"
                ? await stockCountQuery
                    .FromSqlInterpolated(
                        $"SELECT * FROM stock_counts WHERE id = {input.StockCountId} FOR UPDATE")
                    .SingleOrDefaultAsync(cancellationToken)
                : await stockCountQuery
                    .SingleOrDefaultAsync(candidate => candidate.Id == input.StockCountId, cancellationToken);
            if (stockCount is null)
                throw new KeyNotFoundException($"Stock count with ID {input.StockCountId} not found");

            await dbContext.Entry(stockCount)
                .Collection(candidate => candidate.Lines)
                .LoadAsync(cancellationToken);

            if (stockCount.BusinessId != input.BusinessId
                || stockCount.LocationId != input.LocationId
                || stockCount.LocationType != input.LocationType)
            {
                throw new UnauthorizedAccessException(
                    "You are not authorized to access this stock count.");
            }

            if (stockCount.Status != StockCount.StatusDraft)
            {
                throw new InvalidOperationException(
                    "Excel upload is only allowed while stock count is in Draft status.");
            }

            if (await dbContext.StockCountSubmissionJobs.AnyAsync(
                    job => job.StockCountId == stockCount.Id
                        && Enumerable.Contains(StockCountSubmissionJobStatus.Active, job.Status),
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "Excel upload is not allowed while a Mobile stock take submission is in progress.");
            }

            var linesById = stockCount.Lines.ToDictionary(line => line.Id);
            var changed = false;
            foreach (var update in input.Lines)
            {
                if (!linesById.TryGetValue(update.LineId, out var line))
                {
                    throw new UnauthorizedAccessException(
                        $"Stock count line {update.LineId} does not belong to stock count {stockCount.Id}.");
                }

                if (line.PhysicalStock != update.PhysicalStock
                    || line.Difference != update.Difference
                    || !string.Equals(line.Remarks, update.Remarks, StringComparison.Ordinal))
                {
                    changed = true;
                    line.PhysicalStock = update.PhysicalStock;
                    line.Difference = update.Difference;
                    line.Remarks = update.Remarks;
                }
            }

            if (changed)
                stockCount.CountRevision++;

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            logger.LogInformation(
                "Stock count {StockCountNo} uploaded by user {UserId}",
                stockCount.StockCountNo,
                input.ActorUserId);
            return stockCount.Id;
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
}
