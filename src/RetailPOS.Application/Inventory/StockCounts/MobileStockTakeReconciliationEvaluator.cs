using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Inventory.StockCounts;

public sealed record ReconciliationBaselineEvidence(
    string ConsistencyStatus,
    DateTime? ReadStartedAtUtc,
    DateTime? ReadCompletedAtUtc,
    DateTime? SubmittedAtUtc);

public sealed record ReconciliationLineEvidence(
    long VariantId,
    decimal BaselineQuantity,
    decimal? CountedQuantity,
    DateTime? CountObservedNoLaterThanUtc,
    decimal CurrentInventoryQuantity);

public sealed record ReconciliationLedgerEntry(
    long VariantId,
    string TransactionType,
    int QtyIn,
    int QtyOut,
    int BalanceAfter,
    long MovementSequence,
    DateTime CreatedAtUtc);

/// <summary>
/// Pure, deterministic evaluation of whether the existing StockLedger/Inventory evidence supports
/// treating <c>counted - baseline</c> as a variance. It does not invent movements, merge policies or lease
/// rules: whenever the records cannot prove consistency it returns <c>Blocked</c> or <c>Unresolved</c>.
///
/// Rules, per counted line (variant at the Outlet):
/// 1. The last ledger row created before the baseline read started must carry a balance equal to the
///    baseline quantity (or the variant has no ledger and a zero baseline).
/// 2. Ledger rows from the baseline read start must have strictly increasing sequence numbers, non-decreasing
///    creation times (a row written with an earlier explicit creation time than its predecessor is a
///    backdating anomaly) and an unbroken BalanceAfter chain.
/// 3. The final ledger balance must equal the current Inventory quantity (otherwise a movement is missing
///    from the ledger).
/// 4. Any ledger row created between the baseline read start and the latest instant the count may have been
///    observed (server receipt of the counted value, never later than ERP submission) cannot be ordered
///    against the physical count and leaves the line Unresolved. Rows after that instant are after the
///    observation and do not affect the counted delta.
/// </summary>
public static class MobileStockTakeReconciliationEvaluator
{
    private const string SnapshotConsistency = "InventoryStatementSnapshot";

    public static MobileStockTakeReconciliationEvaluation NotEvaluated(string reason, DateTime evaluatedAtUtc) =>
        new(
            MobileStockTakeReconciliationStatus.NotEvaluated,
            [new MobileStockTakeReconciliationReason(reason, 0)],
            MobileStockTakeMovementSummary.Empty,
            0,
            evaluatedAtUtc);

    public static MobileStockTakeReconciliationEvaluation Evaluate(
        ReconciliationBaselineEvidence baseline,
        IReadOnlyCollection<ReconciliationLineEvidence> lines,
        IReadOnlyDictionary<long, ReconciliationLedgerEntry> lastBeforeBaseline,
        IReadOnlyCollection<ReconciliationLedgerEntry> entriesFromBaseline,
        DateTime evaluatedAtUtc)
    {
        var counted = lines.Where(line => line.CountedQuantity.HasValue).ToArray();
        if (baseline.ConsistencyStatus != SnapshotConsistency
            || baseline.ReadStartedAtUtc is null
            || baseline.ReadCompletedAtUtc is null
            || baseline.ReadCompletedAtUtc < baseline.ReadStartedAtUtc)
        {
            return Blocked(MobileStockTakeReconciliationReasonCodes.BaselineNotVerifiable, counted.Length, evaluatedAtUtc);
        }

        if (baseline.SubmittedAtUtc is null)
            return Blocked(MobileStockTakeReconciliationReasonCodes.SubmissionTimeMissing, counted.Length, evaluatedAtUtc);
        if (counted.Length == 0)
            return Blocked(MobileStockTakeReconciliationReasonCodes.NoCountedLines, 0, evaluatedAtUtc);

        var entriesByVariant = entriesFromBaseline
            .GroupBy(entry => entry.VariantId)
            .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.MovementSequence).ToArray());
        var blocked = new Dictionary<string, int>(StringComparer.Ordinal);
        var windowLines = 0;
        int sales = 0, returns = 0, transfers = 0, adjustments = 0, receipts = 0, other = 0, postCount = 0;

        foreach (var line in counted)
        {
            lastBeforeBaseline.TryGetValue(line.VariantId, out var previous);
            var entries = entriesByVariant.GetValueOrDefault(line.VariantId) ?? [];

            if (HasOrderAnomaly(previous, entries))
            {
                Increment(blocked, MobileStockTakeReconciliationReasonCodes.MovementOrderAnomaly);
                continue;
            }

            var expectedAtBaseline = previous?.BalanceAfter ?? 0;
            if (previous is null && line.BaselineQuantity != 0)
            {
                Increment(blocked, MobileStockTakeReconciliationReasonCodes.BaselineLedgerEvidenceMissing);
                continue;
            }

            if (line.BaselineQuantity != expectedAtBaseline)
            {
                Increment(blocked, MobileStockTakeReconciliationReasonCodes.BaselineLedgerMismatch);
                continue;
            }

            var running = expectedAtBaseline;
            var chainBroken = false;
            foreach (var entry in entries)
            {
                if (running + entry.QtyIn - entry.QtyOut != entry.BalanceAfter)
                {
                    chainBroken = true;
                    break;
                }

                running = entry.BalanceAfter;
            }

            if (chainBroken)
            {
                Increment(blocked, MobileStockTakeReconciliationReasonCodes.MovementBalanceChainBroken);
                continue;
            }

            if (running != line.CurrentInventoryQuantity)
            {
                Increment(blocked, MobileStockTakeReconciliationReasonCodes.InventoryLedgerMismatch);
                continue;
            }

            var upperBound = line.CountObservedNoLaterThanUtc is { } observed
                && observed < baseline.SubmittedAtUtc.Value
                    ? observed
                    : baseline.SubmittedAtUtc.Value;
            // A cost correction moves no stock, so it says nothing about the count.
            var stockEntries = entries
                .Where(entry => entry.TransactionType != StockLedgerTransactionType.CostCorrection)
                .ToArray();
            var inWindow = stockEntries.Where(entry => entry.CreatedAtUtc <= upperBound).ToArray();
            postCount += stockEntries.Length - inWindow.Length;
            if (inWindow.Length == 0)
                continue;

            windowLines++;
            foreach (var entry in inWindow)
            {
                switch (entry.TransactionType)
                {
                    case StockLedgerTransactionType.Sale: sales++; break;
                    case StockLedgerTransactionType.Return:
                    case StockLedgerTransactionType.Exchange: returns++; break;
                    case StockLedgerTransactionType.TransferIn:
                    case StockLedgerTransactionType.TransferOut:
                    case StockLedgerTransactionType.ReturnTransferIn:
                    case StockLedgerTransactionType.ReturnTransferOut:
                    case StockLedgerTransactionType.TransferRejection:
                    case StockLedgerTransactionType.TransferDiscrepancy: transfers++; break;
                    case StockLedgerTransactionType.Adjustment: adjustments++; break;
                    case StockLedgerTransactionType.Grn: receipts++; break;
                    default: other++; break;
                }
            }
        }

        var reasons = blocked
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new MobileStockTakeReconciliationReason(pair.Key, pair.Value))
            .ToList();
        var summary = new MobileStockTakeMovementSummary(
            sales, returns, transfers, adjustments, receipts, other, postCount);
        if (reasons.Count > 0)
        {
            if (windowLines > 0)
                reasons.Add(new MobileStockTakeReconciliationReason(
                    MobileStockTakeReconciliationReasonCodes.MovementDuringCountWindow, windowLines));
            return new MobileStockTakeReconciliationEvaluation(
                MobileStockTakeReconciliationStatus.Blocked, reasons, summary, counted.Length, evaluatedAtUtc);
        }

        if (windowLines > 0)
        {
            reasons.Add(new MobileStockTakeReconciliationReason(
                MobileStockTakeReconciliationReasonCodes.MovementDuringCountWindow, windowLines));
            return new MobileStockTakeReconciliationEvaluation(
                MobileStockTakeReconciliationStatus.Unresolved, reasons, summary, counted.Length, evaluatedAtUtc);
        }

        return new MobileStockTakeReconciliationEvaluation(
            MobileStockTakeReconciliationStatus.NoConflictDetected, reasons, summary, counted.Length, evaluatedAtUtc);
    }

    private static bool HasOrderAnomaly(
        ReconciliationLedgerEntry? previous,
        IReadOnlyList<ReconciliationLedgerEntry> entries)
    {
        var lastSequence = previous?.MovementSequence ?? long.MinValue;
        var lastCreatedAt = previous?.CreatedAtUtc ?? DateTime.MinValue;
        foreach (var entry in entries)
        {
            if (entry.MovementSequence <= lastSequence || entry.CreatedAtUtc < lastCreatedAt)
                return true;
            lastSequence = entry.MovementSequence;
            lastCreatedAt = entry.CreatedAtUtc;
        }

        return false;
    }

    private static void Increment(Dictionary<string, int> counts, string code) =>
        counts[code] = counts.GetValueOrDefault(code) + 1;

    private static MobileStockTakeReconciliationEvaluation Blocked(
        string code,
        int lineCount,
        DateTime evaluatedAtUtc) =>
        new(
            MobileStockTakeReconciliationStatus.Blocked,
            [new MobileStockTakeReconciliationReason(code, lineCount)],
            MobileStockTakeMovementSummary.Empty,
            lineCount,
            evaluatedAtUtc);
}

public static class MobileStockTakeResultsPolicy
{
    public static bool IsApprovedState(string status) =>
        status is StockCount.StatusApproved
            or StockCount.StatusAdjustmentGenerated
            or StockCount.StatusCompleted;

    /// <summary>
    /// Final results are exposed only for an ERP-approved count whose approval is recorded and whose movement
    /// evidence shows no conflict. Draft, Submitted and Rejected counts stay blind.
    /// </summary>
    public static (string Availability, IReadOnlyList<string> Reasons) Decide(
        string status,
        DateTime? approvedAtUtc,
        MobileStockTakeReconciliationEvaluation? evaluation,
        int totalLines,
        int countedLines,
        int inconsistentDifferenceLines)
    {
        if (!IsApprovedState(status))
        {
            return (
                MobileStockTakeResultsAvailability.NotAvailable,
                [MobileStockTakeResultsReasonCodes.NotApproved]);
        }

        var reasons = new List<string>();
        if (approvedAtUtc is null)
            reasons.Add(MobileStockTakeResultsReasonCodes.ApprovalEvidenceMissing);
        if (totalLines == 0 || countedLines != totalLines)
            reasons.Add(MobileStockTakeResultsReasonCodes.LinesUncounted);
        if (inconsistentDifferenceLines > 0)
            reasons.Add(MobileStockTakeResultsReasonCodes.DifferenceInconsistent);
        if (evaluation is null
            || evaluation.Status != MobileStockTakeReconciliationStatus.NoConflictDetected)
        {
            reasons.AddRange(evaluation?.Reasons.Select(reason => reason.Code)
                ?? [MobileStockTakeReconciliationReasonCodes.BaselineNotVerifiable]);
        }

        return reasons.Count == 0
            ? (MobileStockTakeResultsAvailability.Available, [])
            : (MobileStockTakeResultsAvailability.Withheld, reasons.Distinct().ToArray());
    }
}

public static class MobileStockTakeLifecycleStageResolver
{
    public static string Resolve(
        string status,
        long countRevision,
        string? latestJobStatus,
        long? latestJobAcceptedRevision) =>
        status switch
        {
            StockCount.StatusDraft when latestJobStatus is not null
                && StockCountSubmissionJobStatus.IsActive(latestJobStatus) =>
                MobileStockTakeLifecycleStages.SubmissionInProgress,
            StockCount.StatusDraft when latestJobStatus == StockCountSubmissionJobStatus.Failed
                && latestJobAcceptedRevision == countRevision =>
                MobileStockTakeLifecycleStages.SubmissionFailed,
            StockCount.StatusDraft => MobileStockTakeLifecycleStages.Counting,
            StockCount.StatusSubmitted => MobileStockTakeLifecycleStages.SubmittedForErpReview,
            StockCount.StatusRejected => MobileStockTakeLifecycleStages.RejectedByErp,
            StockCount.StatusApproved => MobileStockTakeLifecycleStages.ApprovedByErp,
            StockCount.StatusAdjustmentGenerated => MobileStockTakeLifecycleStages.AdjustmentDraftGenerated,
            StockCount.StatusCompleted => MobileStockTakeLifecycleStages.Completed,
            _ => MobileStockTakeLifecycleStages.Unknown
        };
}
