using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public sealed class StockCountLifecycleStore(RetailPOSDbContext dbContext)
    : IStockCountLifecycleStore
{
    public Task SubmitAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        long actorUserId,
        CancellationToken cancellationToken) =>
        ApplyAsync(
            stockCountId,
            businessId,
            locationId,
            locationType,
            stockCount =>
            {
                if (stockCount.Status != StockCount.StatusDraft)
                {
                    throw new InvalidOperationException("Only Draft stock counts can be submitted.");
                }

                var hasUploadedCounts = stockCount.Lines.Any(line =>
                    line.PhysicalStock.HasValue
                    || !string.IsNullOrWhiteSpace(line.Remarks));
                if (!hasUploadedCounts)
                {
                    throw new InvalidOperationException(
                        "Upload completed Excel before submitting stock count.");
                }

                stockCount.Status = StockCount.StatusSubmitted;
                stockCount.SubmittedBy = actorUserId;
                stockCount.SubmittedAt = DateTime.UtcNow;
                stockCount.RejectedBy = null;
                stockCount.RejectedAt = null;
                stockCount.RejectionReason = null;
            },
            cancellationToken);

    public Task RejectAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        long actorUserId,
        string? reason,
        CancellationToken cancellationToken) =>
        ApplyAsync(
            stockCountId,
            businessId,
            locationId,
            locationType,
            stockCount =>
            {
                if (stockCount.Status != StockCount.StatusSubmitted)
                {
                    throw new InvalidOperationException("Only Submitted stock counts can be rejected.");
                }

                stockCount.Status = StockCount.StatusRejected;
                stockCount.RejectedBy = actorUserId;
                stockCount.RejectedAt = DateTime.UtcNow;
                stockCount.RejectionReason = string.IsNullOrWhiteSpace(reason)
                    ? null
                    : reason.Trim();
            },
            cancellationToken);

    public Task ReopenAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        long actorUserId,
        CancellationToken cancellationToken) =>
        ApplyAsync(
            stockCountId,
            businessId,
            locationId,
            locationType,
            stockCount =>
            {
                if (stockCount.Status != StockCount.StatusRejected)
                {
                    throw new InvalidOperationException("Only Rejected stock counts can be reopened.");
                }

                stockCount.Status = StockCount.StatusDraft;
                stockCount.RejectedBy = null;
                stockCount.RejectedAt = null;
                stockCount.RejectionReason = null;
            },
            cancellationToken);

    private async Task ApplyAsync(
        long stockCountId,
        long businessId,
        long locationId,
        string locationType,
        Action<StockCount> applyTransition,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        // Join a caller-owned transaction (e.g. the Mobile submission worker's audited boundary) so the
        // ERP transition and the caller's durable outcome commit or roll back together.
        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null)
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
                        $"SELECT * FROM stock_counts WHERE id = {stockCountId} FOR UPDATE")
                    .SingleOrDefaultAsync(cancellationToken)
                : await stockCountQuery
                    .SingleOrDefaultAsync(candidate => candidate.Id == stockCountId, cancellationToken);
            if (stockCount is null)
                throw new KeyNotFoundException($"Stock count with ID {stockCountId} not found");

            await dbContext.Entry(stockCount)
                .Collection(candidate => candidate.Lines)
                .LoadAsync(cancellationToken);

            if (stockCount.BusinessId != businessId
                || stockCount.LocationId != locationId
                || stockCount.LocationType != locationType)
            {
                throw new UnauthorizedAccessException(
                    "You are not authorized to access this stock count.");
            }

            applyTransition(stockCount);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
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
