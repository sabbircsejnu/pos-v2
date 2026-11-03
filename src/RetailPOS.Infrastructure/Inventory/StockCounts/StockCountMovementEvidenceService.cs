using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockCounts;

public sealed record StockCountMovementEvidence(
    MobileStockTakeReconciliationEvaluation Evaluation,
    int TotalLines,
    int CountedLines,
    int InconsistentDifferenceLines);

/// <summary>
/// Deterministic StockLedger/Inventory evidence for a StockCount. Shared by the read-only Mobile review
/// endpoints and by the automatic adjustment worker so both apply exactly the same gate. It never writes.
/// </summary>
public sealed class StockCountMovementEvidenceService(
    RetailPOSDbContext dbContext,
    TimeProvider clock)
{
    public async Task<StockCountMovementEvidence> EvaluateAsync(
        StockCount session,
        CancellationToken cancellationToken)
    {
        var evaluatedAt = clock.GetUtcNow().UtcDateTime;
        var lines = await dbContext.StockCountLines.AsNoTracking()
            .Where(line => line.StockCountId == session.Id)
            .Select(line => new { line.VariantId, line.CurrentStock, line.PhysicalStock, line.Difference })
            .ToArrayAsync(cancellationToken);
        var counted = lines.Count(line => line.PhysicalStock.HasValue);
        var inconsistent = lines.Count(line =>
            line.PhysicalStock.HasValue
            && line.Difference != line.PhysicalStock.Value - line.CurrentStock);

        if (session.Status == StockCount.StatusDraft)
        {
            return new StockCountMovementEvidence(
                MobileStockTakeReconciliationEvaluator.NotEvaluated(
                    MobileStockTakeReconciliationReasonCodes.SessionNotSubmitted, evaluatedAt),
                lines.Length,
                counted,
                inconsistent);
        }

        if (session.Status == StockCount.StatusRejected)
        {
            return new StockCountMovementEvidence(
                MobileStockTakeReconciliationEvaluator.NotEvaluated(
                    MobileStockTakeReconciliationReasonCodes.SessionRejected, evaluatedAt),
                lines.Length,
                counted,
                inconsistent);
        }

        var baseline = new ReconciliationBaselineEvidence(
            session.BaselineConsistencyStatus,
            UtcTime.AsUtc(session.BaselineInventoryReadStartedAtUtc),
            UtcTime.AsUtc(session.BaselineInventoryReadCompletedAtUtc),
            UtcTime.AsUtc(session.SubmittedAt));
        if (baseline.ReadStartedAtUtc is null || baseline.SubmittedAtUtc is null)
        {
            var evaluation = MobileStockTakeReconciliationEvaluator.Evaluate(
                baseline, [], new Dictionary<long, ReconciliationLedgerEntry>(), [], evaluatedAt);
            return new StockCountMovementEvidence(evaluation, lines.Length, counted, inconsistent);
        }

        var locationId = session.LocationId;
        var locationType = session.LocationType.ToLowerInvariant();
        var startedAt = baseline.ReadStartedAtUtc.Value;
        var currentInventory = await dbContext.Inventories.AsNoTracking()
            .Where(inventory => inventory.LocationId == locationId && inventory.LocationType == locationType)
            .GroupBy(inventory => inventory.VariantId)
            .Select(group => new { VariantId = group.Key, Quantity = group.Sum(inventory => inventory.Quantity) })
            .ToDictionaryAsync(row => row.VariantId, row => (decimal)row.Quantity, cancellationToken);
        var latestReceipts = (await dbContext.StockCountOperationReceipts.AsNoTracking()
                .Where(receipt => receipt.StockCountId == session.Id
                                  && receipt.Outcome == StockCountOperationOutcome.Accepted)
                .Select(receipt => new
                {
                    receipt.ProductVariantId,
                    receipt.CountedQuantity,
                    receipt.ServerReceivedAtUtc,
                    receipt.AcceptedRevision
                })
                .ToArrayAsync(cancellationToken))
            .GroupBy(receipt => receipt.ProductVariantId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(receipt => receipt.AcceptedRevision).First());
        var fromBaseline = (await dbContext.StockLedgers.AsNoTracking()
                .Where(entry => entry.LocationId == locationId
                                && entry.LocationType == locationType
                                && entry.CreatedAt >= startedAt)
                .Select(entry => new ReconciliationLedgerEntry(
                    entry.VariantId,
                    entry.TransactionType,
                    entry.QtyIn,
                    entry.QtyOut,
                    entry.BalanceAfter,
                    entry.MovementSequence,
                    entry.CreatedAt))
                .ToArrayAsync(cancellationToken))
            .Select(entry => entry with { CreatedAtUtc = UtcTime.AsUtc(entry.CreatedAtUtc) })
            .ToArray();
        var beforeBaseline = (await dbContext.StockLedgers.AsNoTracking()
                .Where(entry => entry.LocationId == locationId
                                && entry.LocationType == locationType
                                && entry.CreatedAt < startedAt
                                && entry.MovementSequence == dbContext.StockLedgers
                                    .Where(other => other.LocationId == locationId
                                                    && other.LocationType == locationType
                                                    && other.VariantId == entry.VariantId
                                                    && other.CreatedAt < startedAt)
                                    .Max(other => other.MovementSequence))
                .Select(entry => new ReconciliationLedgerEntry(
                    entry.VariantId,
                    entry.TransactionType,
                    entry.QtyIn,
                    entry.QtyOut,
                    entry.BalanceAfter,
                    entry.MovementSequence,
                    entry.CreatedAt))
                .ToArrayAsync(cancellationToken))
            .Select(entry => entry with { CreatedAtUtc = UtcTime.AsUtc(entry.CreatedAtUtc) })
            .GroupBy(entry => entry.VariantId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(entry => entry.MovementSequence)
                    .ThenByDescending(entry => entry.CreatedAtUtc)
                    .ThenByDescending(entry => entry.BalanceAfter)
                    .First());

        var evidenceLines = lines.Select(line =>
        {
            DateTime? observedNoLaterThan = null;
            if (line.PhysicalStock.HasValue
                && latestReceipts.TryGetValue(line.VariantId, out var receipt)
                && receipt.CountedQuantity == line.PhysicalStock.Value)
            {
                observedNoLaterThan = UtcTime.AsUtc(receipt.ServerReceivedAtUtc);
            }

            return new ReconciliationLineEvidence(
                line.VariantId,
                line.CurrentStock,
                line.PhysicalStock,
                observedNoLaterThan,
                currentInventory.GetValueOrDefault(line.VariantId));
        }).ToArray();

        return new StockCountMovementEvidence(
            MobileStockTakeReconciliationEvaluator.Evaluate(
                baseline, evidenceLines, beforeBaseline, fromBaseline, evaluatedAt),
            lines.Length,
            counted,
            inconsistent);
    }
}
